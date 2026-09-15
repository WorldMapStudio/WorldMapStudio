using System;
using System.Collections.Generic;
using System.Text;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// The editor's console output, tailed live from <see cref="ConsoleLog"/>: filter by level and text,
/// clear the view, copy what's shown, and follow the tail. Hover a line for the stack trace the engine
/// printed under it; right-click to copy the whole entry.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class LogWindow : Window
{
    public override string? Category => "Debug";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.D, ShortcutModifiers.Alt);

    private static readonly NVector4 ErrorColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 WarningColor = new(1.0f, 0.72f, 0.22f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.60f, 0.60f, 0.60f, 1.0f);

    private readonly List<LogEntry> _visible = [];

    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private bool _autoScroll = true;
    private bool _wrap;
    private string _filter = string.Empty;
    private string[] _filterTokens = [];

    public LogWindow(WindowManager manager)
        : base("Log", startOpen: false, defaultSize: new NVector2(760.0f, 360.0f))
    {
    }

    protected override void OnBeforeDraw() => ConsoleLog.Pump();

    protected override void DrawContent()
    {
        DrawToolbar();
        ImGui.Separator();
        DrawList();
        ImGui.Separator();
        DrawFooter();
    }

    private void DrawToolbar()
    {
        (int info, int warning, int error) = ConsoleLog.Counts;

        ImGui.Checkbox($"Info ({info})###log-info", ref _showInfo);
        ImGui.SameLine();
        PushEnabledColor(_showWarnings && warning > 0, WarningColor);
        ImGui.Checkbox($"Warnings ({warning})###log-warn", ref _showWarnings);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        PushEnabledColor(_showErrors && error > 0, ErrorColor);
        ImGui.Checkbox($"Errors ({error})###log-error", ref _showErrors);
        ImGui.PopStyleColor();

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            ConsoleLog.Clear();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Copy"))
        {
            CopyVisible();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Follow", ref _autoScroll);
        ImGui.SameLine();
        ImGui.Checkbox("Wrap", ref _wrap);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Wrapping disables row virtualisation, so it can be slow on a very long log.");
        }

        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.InputTextWithHint("###log-filter", "Filter — space-separated terms, all must match", ref _filter, 256))
        {
            _filterTokens = _filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private void DrawList()
    {
        RebuildVisible();

        if (!ImGui.BeginChild("log-scroll", new NVector2(0.0f, -ImGui.GetFrameHeightWithSpacing()), true,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        using (ImGuiEx.PushFont(CommonFonts.Monospace))
        {
            if (_visible.Count == 0)
            {
                ImGui.TextDisabled(ConsoleLog.Entries.Count == 0 ? "No output yet." : "Nothing matches the filter.");
            }
            else if (_wrap)
            {
                for (int i = 0; i < _visible.Count; i++)
                {
                    DrawRow(_visible[i], i);
                }
            }
            else
            {
                unsafe
                {
                    var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(_visible.Count);
                    while (clipper.Step())
                    {
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            DrawRow(_visible[i], i);
                        }
                    }

                    clipper.End();
                    clipper.Destroy();
                }
            }
        }

        if (_autoScroll)
        {
            ImGui.SetScrollY(ImGui.GetScrollMaxY());
        }

        ImGui.EndChild();
    }

    private void DrawRow(LogEntry entry, int index)
    {
        ImGui.PushID(index);

        ImGui.TextDisabled(entry.Time.ToString("HH:mm:ss"));
        ImGui.SameLine();

        NVector4? color = entry.Level switch
        {
            LogLevel.Error => ErrorColor,
            LogLevel.Warning => WarningColor,
            _ => null,
        };

        if (_wrap)
        {
            ImGui.PushTextWrapPos(0.0f);
        }

        if (color is { } c)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, c);
        }

        string marker = entry.Detail is null ? string.Empty : "» ";
        ImGui.TextUnformatted(marker + entry.Message);

        if (color is not null)
        {
            ImGui.PopStyleColor();
        }

        if (_wrap)
        {
            ImGui.PopTextWrapPos();
        }

        if (entry.Detail is { } detail && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Shorten(detail, 1600) + "\n\n(right-click to copy the whole entry)");
        }

        if (ImGui.BeginPopupContextItem("row-menu"))
        {
            if (ImGui.MenuItem("Copy line"))
            {
                ImGui.SetClipboardText(entry.Message);
            }

            if (entry.Detail is not null && ImGui.MenuItem("Copy entry with trace"))
            {
                ImGui.SetClipboardText(entry.Raw);
            }

            if (ImGui.MenuItem("Copy all shown"))
            {
                CopyVisible();
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private void DrawFooter()
    {
        if (ConsoleLog.Status is { } status)
        {
            ImGui.TextColored(WarningColor, status);
            return;
        }

        ImGui.TextColored(MutedColor, $"{ConsoleLog.Entries.Count} lines  ·  {_visible.Count} shown  ·  {ConsoleLog.LogPath}");
    }

    private void RebuildVisible()
    {
        _visible.Clear();
        foreach (LogEntry entry in ConsoleLog.Entries)
        {
            if (!LevelVisible(entry.Level) || !MatchesFilter(entry))
            {
                continue;
            }

            _visible.Add(entry);
        }
    }

    private bool LevelVisible(LogLevel level) => level switch
    {
        LogLevel.Warning => _showWarnings,
        LogLevel.Error => _showErrors,
        _ => _showInfo,
    };

    private bool MatchesFilter(LogEntry entry)
    {
        foreach (string token in _filterTokens)
        {
            if (entry.Message.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0
                && (entry.Detail is null || entry.Detail.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }
        }

        return true;
    }

    private void CopyVisible()
    {
        var text = new StringBuilder();
        foreach (LogEntry entry in _visible)
        {
            text.Append(entry.Time.ToString("HH:mm:ss")).Append(' ').AppendLine(entry.Raw);
        }

        ImGui.SetClipboardText(text.ToString());
    }

    private static void PushEnabledColor(bool active, NVector4 color) =>
        ImGui.PushStyleColor(ImGuiCol.Text, active ? color : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "\n…";
}
