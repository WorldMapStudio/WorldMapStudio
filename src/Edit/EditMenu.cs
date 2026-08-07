using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Edit" menu: undo/redo plus committing or aborting the active edit session. Also handles the
/// Ctrl+Z / Ctrl+Y shortcuts (ignored while a text field has focus). Self-registers with
/// <see cref="MenuBarManager"/>, sitting between File and Window.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class EditMenu : IMainMenu
{
    private readonly EditSessionManager _sessions;

    public float Priority => 0.5f;

    public EditMenu(MenuBarManager manager)
    {
        _sessions = manager.Context.EditSessions;
    }

    public void Draw()
    {
        HandleShortcuts();

        EditSession session = _sessions.Active;
        UndoHistory history = session.History;

        ImGuiEx.Menu("Edit", () =>
        {
            if (ImGui.MenuItem("Undo", "Ctrl+Z", false, history.CanUndo))
            {
                _sessions.Undo();
            }

            if (ImGui.MenuItem("Redo", "Ctrl+Y", false, history.CanRedo))
            {
                _sessions.Redo();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Commit Session", string.Empty, false, session.IsDirty))
            {
                _sessions.Commit();
            }

            if (ImGui.MenuItem("Abort Session", string.Empty, false, session.IsDirty))
            {
                _sessions.Abort();
            }
        });
    }

    private void HandleShortcuts()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (io.WantTextInput || !io.KeyCtrl)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Z, false))
        {
            _sessions.Undo();
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Y, false))
        {
            _sessions.Redo();
        }
    }
}
