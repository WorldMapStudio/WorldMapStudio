using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// "Open Map…": opens the map picker. First snapshots the live viewport as the current map's preview,
/// so the grid always shows where you last were in each map.
/// </summary>
[Subsystem(nameof(MapMenu))]
public sealed class OpenMapMenuItem : IMenuItem
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _shortcut;
    private readonly ModalDialogHost<MapSelectDialog, MapSystem> _selectModal;

    public OpenMapMenuItem(MapMenu menu)
    {
        _context = menu.Context;
        _selectModal = new("SelectMap", () => new MapSelectDialog(_context), new Vector2(700, 0));
        _shortcut = _context.Shortcuts.Register(
            "map.open",
            "Map",
            "Open Map",
            new KeyboardShortcut(ImGuiKey.M, ShortcutModifiers.Alt),
            Open);
    }

    public void Draw()
    {
        if (ImGui.MenuItem("Open Map…", _shortcut.ShortcutLabel))
        {
            Open();
        }
    }

    // The picker is a popup, so it has to be opened and drawn at the root level rather than inside
    // the menu's ID stack — hence the overlay pass instead of drawing it from Draw().
    public void DrawOverlay()
    {
        MapSelectDialog? operation = _selectModal._item;
        if (_selectModal.Draw(_context.Maps, true, ImGuiWindowFlags.None) == ModalDialogState.Confirmed
            && operation?.Selected is { } map)
        {
            _context.Maps.Enter(map);
        }
    }

    private void Open()
    {
        _context.Maps.CaptureThumbnail(_context.Maps.CurrentMap);
        _selectModal.Show();
    }
}
