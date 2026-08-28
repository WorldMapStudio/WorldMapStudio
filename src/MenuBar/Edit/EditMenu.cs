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
    private readonly ShortcutSystem _shortcuts;
    private readonly ShortcutAction _undo;
    private readonly ShortcutAction _redo;
    private readonly ShortcutAction _commit;
    private readonly ShortcutAction _abort;

    public float Priority => 0.5f;

    public EditMenu(MenuBarManager manager)
    {
        _sessions = manager.Context.EditSessions;
        _shortcuts = manager.Context.Shortcuts;
        _undo = _shortcuts.Register(
            "edit.undo",
            "Edit",
            "Undo",
            new KeyboardShortcut(ImGuiKey.Z, ShortcutModifiers.Ctrl),
            _sessions.Undo,
            () => _sessions.Active.History.CanUndo);
        _redo = _shortcuts.Register(
            "edit.redo",
            "Edit",
            "Redo",
            new KeyboardShortcut(ImGuiKey.Y, ShortcutModifiers.Ctrl),
            _sessions.Redo,
            () => _sessions.Active.History.CanRedo);
        _commit = _shortcuts.Register(
            "edit.commit-session",
            "Edit",
            "Commit Session",
            new KeyboardShortcut(ImGuiKey.C, ShortcutModifiers.Alt),
            _sessions.Commit,
            () => _sessions.Active.IsDirty);
        _abort = _shortcuts.Register(
            "edit.abort-session",
            "Edit",
            "Abort Session",
            new KeyboardShortcut(ImGuiKey.A, ShortcutModifiers.Alt),
            _sessions.Abort,
            () => _sessions.Active.IsDirty);
    }

    public void Draw()
    {
        EditSession session = _sessions.Active;
        UndoHistory history = session.History;

        ImGuiEx.Menu("Edit", () =>
        {
            if (ImGui.MenuItem("Undo", _undo.ShortcutLabel, false, history.CanUndo))
            {
                _sessions.Undo();
            }

            if (ImGui.MenuItem("Redo", _redo.ShortcutLabel, false, history.CanRedo))
            {
                _sessions.Redo();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Commit Session", _commit.ShortcutLabel, false, session.IsDirty))
            {
                _sessions.Commit();
            }

            if (ImGui.MenuItem("Abort Session", _abort.ShortcutLabel, false, session.IsDirty))
            {
                _sessions.Abort();
            }
        });
    }
}
