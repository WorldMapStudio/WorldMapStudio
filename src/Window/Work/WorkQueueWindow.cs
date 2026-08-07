using System.Collections.Generic;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class WorkQueueWindow : Window
{
    private static readonly NVector4 QueuedColor = new(0.85f, 0.78f, 0.35f, 1.0f);
    private static readonly NVector4 ExecutingColor = new(0.42f, 0.72f, 1.0f, 1.0f);
    private static readonly NVector4 CompletedColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 FaultedColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 CancelledColor = new(0.70f, 0.70f, 0.70f, 1.0f);

    private bool _showFinished = true;

    public WorkQueueWindow(WindowManager manager) : base("Work Queue", defaultSize: new NVector2(760.0f, 520.0f))
    {
    }

    protected override void DrawContent()
    {
        WorkSnapshot[] items = WorkQueue.Snapshot();

        DrawSummary(items);
        ImGui.Separator();
        DrawByKind(items);
        ImGui.Separator();
        DrawControls();
        DrawTable(items);
    }

    private static void DrawSummary(WorkSnapshot[] items)
    {
        int queuedBg = 0, queuedMain = 0, execBg = 0, execMain = 0, completed = 0, faulted = 0, cancelled = 0;
        foreach (WorkSnapshot item in items)
        {
            switch (item.State)
            {
                case WorkState.Queued when item.Thread == WorkThread.Background: queuedBg++; break;
                case WorkState.Queued: queuedMain++; break;
                case WorkState.Executing when item.Thread == WorkThread.Background: execBg++; break;
                case WorkState.Executing: execMain++; break;
                case WorkState.Completed: completed++; break;
                case WorkState.Faulted: faulted++; break;
                case WorkState.Cancelled: cancelled++; break;
            }
        }

        ImGui.Text($"Workers: {WorkQueue.WorkerCount}");
        ImGui.Text($"Queued:    {queuedBg + queuedMain}  (background {queuedBg}, main {queuedMain})");
        ImGui.Text($"Executing: {execBg + execMain}  (background {execBg}, main {execMain})");
        ImGui.SameLine();
        ImGui.TextDisabled($"   |   done {completed}, faulted {faulted}, cancelled {cancelled}");
    }

    private static void DrawByKind(WorkSnapshot[] items)
    {
        // Aggregate active work by name so we can see "what kind" of work is in flight.
        Dictionary<string, (int Queued, int Executing)> byKind = new();
        foreach (WorkSnapshot item in items)
        {
            if (!item.IsActive)
            {
                continue;
            }
            byKind.TryGetValue(item.Name, out (int Queued, int Executing) counts);
            if (item.State == WorkState.Executing)
            {
                counts.Executing++;
            }
            else
            {
                counts.Queued++;
            }
            byKind[item.Name] = counts;
        }

        ImGui.Text("Active work by kind");
        if (byKind.Count == 0)
        {
            ImGui.TextDisabled("Nothing queued or executing.");
            return;
        }

        if (ImGui.BeginTable("WorkByKind", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Kind");
            ImGui.TableSetupColumn("Queued");
            ImGui.TableSetupColumn("Executing");
            ImGui.TableHeadersRow();

            foreach ((string name, (int queued, int executing)) in byKind)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(name);
                ImGui.TableNextColumn();
                ImGui.Text(queued.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(executing.ToString());
            }

            ImGui.EndTable();
        }
    }

    private void DrawControls()
    {
        if (ImGui.Button("Clear finished"))
        {
            WorkQueue.ClearFinished();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Show finished", ref _showFinished);
    }

    private void DrawTable(WorkSnapshot[] items)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                      ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("WorkItems", 7, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, 44.0f);
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Step");
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 82.0f);
        ImGui.TableSetupColumn("Thread", ImGuiTableColumnFlags.WidthFixed, 82.0f);
        ImGui.TableSetupColumn("Elapsed", ImGuiTableColumnFlags.WidthFixed, 72.0f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 64.0f);
        ImGui.TableHeadersRow();

        foreach (WorkSnapshot item in items)
        {
            if (!_showFinished && item.IsFinished)
            {
                continue;
            }

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(item.Id.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(item.Name);

            ImGui.TableNextColumn();
            if (item.State == WorkState.Faulted && !string.IsNullOrEmpty(item.Error))
            {
                ImGui.TextColored(FaultedColor, item.Error);
            }
            else
            {
                ImGui.Text(string.IsNullOrEmpty(item.Step) ? "-" : item.Step);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(StateColor(item.State), item.State.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(item.Thread.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(FormatElapsed(item.ElapsedSeconds));

            ImGui.TableNextColumn();
            if (item.IsActive)
            {
                ImGui.PushID((int)item.Id);
                if (ImGui.SmallButton("Cancel"))
                {
                    WorkQueue.Cancel(item.Id);
                }
                ImGui.PopID();
            }
        }

        ImGui.EndTable();
    }

    private static NVector4 StateColor(WorkState state) => state switch
    {
        WorkState.Queued => QueuedColor,
        WorkState.Executing => ExecutingColor,
        WorkState.Completed => CompletedColor,
        WorkState.Faulted => FaultedColor,
        WorkState.Cancelled => CancelledColor,
        _ => CompletedColor,
    };

    private static string FormatElapsed(double seconds) =>
        seconds < 1.0 ? $"{seconds * 1000.0:0} ms" : $"{seconds:0.0} s";
}
