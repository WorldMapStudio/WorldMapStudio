#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Runs in-editor <see cref="EditorTestAttribute"/> tests and shows their results grouped by category
/// with colour-coded outcomes, per-test timing, and expandable failure details (message, stack, logs).
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class TestRunnerWindow : Window
{
    public override string? Category => "Developer";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.J, ShortcutModifiers.Alt);

    private static readonly NVector4 PassedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 FailedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 ErroredColor = new(1.0f, 0.62f, 0.28f, 1.0f);
    private static readonly NVector4 SkippedColor = new(0.70f, 0.70f, 0.70f, 1.0f);
    private static readonly NVector4 RunningColor = new(0.42f, 0.72f, 1.0f, 1.0f);
    private static readonly NVector4 NotRunColor = new(0.55f, 0.55f, 0.55f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly EditorContext _context;

    private TestRunner _runner => _context.Tests;

    private string _filter = "";
    private string _parsedFilterText = "";
    private TestFilter _parsedFilter = TestFilter.All;
    private bool _showPassed = true;
    private bool _showSkipped = true;
    private bool _onlyFailures;

    public TestRunnerWindow(WindowManager manager)
        : base("Test Runner", startOpen: false, defaultSize: new NVector2(720.0f, 560.0f))
    {
        _context = manager.Context;
    }

    protected override void DrawContent()
    {
        if (_filter != _parsedFilterText)
        {
            _parsedFilter = TestFilter.Parse(_filter);
            _parsedFilterText = _filter;
        }

        TestResultView[] results = _runner.Snapshot();
        TestRunSummary summary = _runner.Summarize();

        DrawToolbar(results);
        ImGui.Separator();
        DrawSummary(summary);
        ImGui.Separator();
        DrawResults(results);
    }

    private void DrawToolbar(TestResultView[] results)
    {
        bool running = _runner.IsRunning;

        ImGui.BeginDisabled(running);
        if (ImGui.Button("Run All"))
        {
            _runner.RunAll();
        }
        ImGui.SameLine();
        if (ImGui.Button("Run Filtered"))
        {
            _runner.Run(FilteredCases(results));
        }
        ImGui.SameLine();
        if (ImGui.Button("Run Failed"))
        {
            _runner.RunFailed();
        }
        ImGui.SameLine();
        if (ImGui.Button("Rescan"))
        {
            _runner.Rediscover();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!running);
        if (ImGui.Button("Stop"))
        {
            _runner.Stop();
        }
        ImGui.EndDisabled();

        // Filters row.
        ImGui.PushItemWidth(220.0f);
        ImGui.InputTextWithHint("##filter", "Filter by name or category (* for glob)...", ref _filter, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.Checkbox("Passed", ref _showPassed);
        ImGui.SameLine();
        ImGui.Checkbox("Skipped", ref _showSkipped);
        ImGui.SameLine();
        if (ImGui.Checkbox("Only failures", ref _onlyFailures) && _onlyFailures)
        {
            _showPassed = false;
            _showSkipped = false;
        }
    }

    private void DrawSummary(TestRunSummary s)
    {
        ImGui.Text($"{s.Total} tests");
        ImGui.SameLine();
        ImGui.TextColored(MutedColor, "|");
        ImGui.SameLine();
        Count("passed", s.Passed, PassedColor);
        Count("failed", s.Failed, FailedColor);
        Count("errored", s.Errored, ErroredColor);
        Count("skipped", s.Skipped, SkippedColor);
        Count("not run", s.NotRun, NotRunColor);

        ImGui.SameLine();
        ImGui.TextColored(MutedColor, $"   {FormatMs(s.DurationMs)}");

        if (_runner.IsRunning)
        {
            float fraction = s.Total == 0 ? 0.0f : (float)s.Ran / s.Total;
            ImGui.ProgressBar(fraction, new NVector2(-1.0f, 0.0f), $"Running  {s.Ran}/{s.Total}");
        }
        else if (s.Ran > 0)
        {
            NVector4 barColor = s.AllGreen ? PassedColor : FailedColor;
            string label = s.AllGreen ? "All passing" : $"{s.Failed + s.Errored} failing";
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
            ImGui.ProgressBar(1.0f, new NVector2(-1.0f, 0.0f), label);
            ImGui.PopStyleColor();
        }
    }

    private static void Count(string label, int value, NVector4 color)
    {
        ImGui.SameLine();
        NVector4 c = value > 0 ? color : MutedColor;
        ImGui.TextColored(c, $"{value} {label}");
    }

    private void DrawResults(TestResultView[] results)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                                      ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("TestResults", 4, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Test", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 90.0f);
        ImGui.TableSetupColumn("Thread", ImGuiTableColumnFlags.WidthFixed, 78.0f);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 72.0f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        // Group consecutive results by category (Snapshot is already sorted by category then name).
        int i = 0;
        bool anyVisible = false;
        while (i < results.Length)
        {
            string category = results[i].Category;
            int start = i;
            int visibleInGroup = 0;
            int passedInGroup = 0;
            while (i < results.Length && results[i].Category == category)
            {
                if (IsVisible(results[i]))
                {
                    visibleInGroup++;
                    if (results[i].Outcome == TestOutcome.Passed)
                    {
                        passedInGroup++;
                    }
                }
                i++;
            }

            if (visibleInGroup == 0)
            {
                continue;
            }
            anyVisible = true;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            bool open = ImGui.TreeNodeEx(
                $"{category}##cat",
                ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.TableNextColumn();
            ImGui.TextColored(MutedColor, $"{passedInGroup}/{visibleInGroup}");
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();

            if (open)
            {
                for (int j = start; j < start + (i - start); j++)
                {
                    if (IsVisible(results[j]))
                    {
                        DrawTestRow(results[j]);
                    }
                }
                ImGui.TreePop();
            }
        }

        ImGui.EndTable();

        if (!anyVisible)
        {
            ImGui.Spacing();
            ImGui.TextColored(MutedColor, results.Length == 0
                ? "No tests discovered. Mark methods with [EditorTest]."
                : "No tests match the current filter.");
        }
    }

    private void DrawTestRow(TestResultView result)
    {
        ImGui.TableNextRow();
        ImGui.PushID(result.Id);

        ImGui.TableNextColumn();
        ImGuiTreeNodeFlags nodeFlags = ImGuiTreeNodeFlags.SpanFullWidth;
        if (!result.HasDetails)
        {
            nodeFlags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet;
        }
        else if (result.Outcome is TestOutcome.Failed or TestOutcome.Errored)
        {
            nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        NVector4 color = OutcomeColor(result.Outcome);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        bool open = ImGui.TreeNodeEx(result.Name, nodeFlags);
        ImGui.PopStyleColor();

        ImGui.TableNextColumn();
        ImGui.TextColored(color, OutcomeLabel(result.Outcome));

        ImGui.TableNextColumn();
        ImGui.TextColored(MutedColor, result.Thread.ToString());

        ImGui.TableNextColumn();
        ImGui.TextColored(MutedColor, result.Outcome == TestOutcome.NotRun ? "-" : FormatMs(result.DurationMs));

        if (open && result.HasDetails)
        {
            DrawDetails(result, color);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DrawDetails(TestResultView result, NVector4 color)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Indent();

        if (result.Message.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(result.Message);
            ImGui.PopStyleColor();
        }

        if (result.Details.Length > 0 && ImGui.TreeNodeEx("Stack trace##stack", ImGuiTreeNodeFlags.SpanFullWidth))
        {
            ImGui.TextWrapped(result.Details);
            ImGui.TreePop();
        }

        if (result.Logs.Length > 0)
        {
            ImGui.TextColored(MutedColor, "Log:");
            foreach (string line in result.Logs)
            {
                ImGui.BulletText(line);
            }
        }

        ImGui.Unindent();
        // Fill remaining columns so the row background renders cleanly.
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
    }

    private IEnumerable<TestCase> FilteredCases(TestResultView[] results)
    {
        HashSet<string> visibleIds = new();
        foreach (TestResultView r in results)
        {
            if (IsVisible(r))
            {
                visibleIds.Add(r.Id);
            }
        }

        foreach (TestCase c in _runner.Cases)
        {
            if (visibleIds.Contains(c.Id))
            {
                yield return c;
            }
        }
    }

    private bool IsVisible(TestResultView r)
    {
        if (!_parsedFilter.Matches(r.Id))
        {
            return false;
        }

        return r.Outcome switch
        {
            TestOutcome.Passed => _showPassed && !_onlyFailures,
            TestOutcome.Skipped => _showSkipped && !_onlyFailures,
            TestOutcome.NotRun => !_onlyFailures,
            _ => true, // Failed / Errored / Running always shown
        };
    }

    private static NVector4 OutcomeColor(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => PassedColor,
        TestOutcome.Failed => FailedColor,
        TestOutcome.Errored => ErroredColor,
        TestOutcome.Skipped => SkippedColor,
        TestOutcome.Running => RunningColor,
        _ => NotRunColor,
    };

    private static string OutcomeLabel(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => "Passed",
        TestOutcome.Failed => "Failed",
        TestOutcome.Errored => "Errored",
        TestOutcome.Skipped => "Skipped",
        TestOutcome.Running => "Running…",
        _ => "—",
    };

    private static string FormatMs(double ms) =>
        ms >= 1000.0 ? $"{ms / 1000.0:0.00} s" : $"{ms:0.0} ms";
}
