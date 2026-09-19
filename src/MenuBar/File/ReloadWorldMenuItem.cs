using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// "Reload World": drops every loaded entity and catalog and reads them back fresh from the database
/// — the explicit, user-facing door onto <see cref="WorldLifecycle"/>/<see cref="WorldReload"/>.
/// Confirms first when the active edit session is dirty, since a reload aborts it (see
/// <see cref="EditSessionManager.Abort"/>); with nothing dirty there is nothing to lose, so it reloads
/// immediately.
/// </summary>
[Subsystem(nameof(FileMenuManager))]
public sealed class ReloadWorldMenuItem : IMenuItem
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _shortcut;

    private readonly ModalConfirm _confirm = new(
        "Reload World",
        "The active edit session has uncommitted changes, which reloading will revert. Continue?",
        "Reload",
        "Cancel");

    public float Priority => 0.5f;

    public int Section => 1;

    public ReloadWorldMenuItem(FileMenuManager manager)
    {
        _context = manager.Context;
        _shortcut = manager.Shortcuts.Register(
            "file.reload-world",
            "File",
            "Reload World",
            new KeyboardShortcut(ImGuiKey.R, ShortcutModifiers.Ctrl | ShortcutModifiers.Shift),
            Request);
    }

    public void Draw()
    {
        if (ImGui.MenuItem("Reload World", _shortcut.ShortcutLabel))
        {
            Request();
        }
    }

    public void DrawOverlay()
    {
        if (_confirm.Draw(true) == ModalOperationState.Confirmed)
        {
            _context.EditSessions.Abort();
        }
    }

    private void Request()
    {
        if (_context.EditSessions.Active.IsDirty)
        {
            _confirm.Show();
        }
        else
        {
            _context.RequestReload("Reload World");
        }
    }
}
