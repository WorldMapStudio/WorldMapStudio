using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Shows the active edit session's undo/redo timeline and lets clicking an entry jump straight to
/// it. Scoped to the current <see cref="EditSession"/>, so the list clears on commit or abort.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class UndoHistoryWindow : Window
{
    public override string? Category => "Debug";

    private static readonly Vector4 CurrentColor = new(0.42f, 0.85f, 0.46f, 1.0f);

    private readonly EditSessionManager _sessions;

    public UndoHistoryWindow(WindowManager manager)
        : base("Undo History", startOpen: false, defaultSize: new Vector2(280, 360))
    {
        _sessions = manager.Context.EditSessions;
    }

    protected override void DrawContent()
    {
        UndoHistory history = _sessions.Active.History;
        IReadOnlyList<IEditCommand> undo = history.UndoStack;
        IReadOnlyList<IEditCommand> redo = history.RedoStack;

        if (undo.Count == 0 && redo.Count == 0)
        {
            ImGui.TextDisabled("No edits recorded in the current session.");
            return;
        }

        DrawRow("(start)", isCurrent: undo.Count == 0, targetIndex: 0, undo.Count);

        for (int i = 0; i < undo.Count; i++)
        {
            DrawRow(undo[i].Description, isCurrent: i == undo.Count - 1, targetIndex: i + 1, undo.Count);
        }

        for (int i = redo.Count - 1; i >= 0; i--)
        {
            DrawRow(redo[i].Description, isCurrent: false, targetIndex: undo.Count + (redo.Count - i), undo.Count);
        }
    }

    private void DrawRow(string label, bool isCurrent, int targetIndex, int undoCount)
    {
        if (isCurrent)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, CurrentColor);
        }

        bool clicked = ImGui.Selectable($"{label}##{targetIndex}", isCurrent);

        if (isCurrent)
        {
            ImGui.PopStyleColor();
        }

        if (clicked)
        {
            JumpTo(targetIndex, undoCount);
        }
    }

    private void JumpTo(int targetIndex, int undoCount)
    {
        while (undoCount > targetIndex)
        {
            _sessions.Undo();
            undoCount--;
        }

        while (undoCount < targetIndex)
        {
            _sessions.Redo();
            undoCount++;
        }
    }
}
