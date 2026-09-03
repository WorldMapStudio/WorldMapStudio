using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// The "Map" menu: shows which map is open and opens the map picker. Opening the picker first
/// snapshots the live viewport as the current map's preview, so the grid always shows where you
/// last were in each map. Self-registers with <see cref="MenuBarManager"/>, between Scene and Window.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class MapMenu : IMainMenu
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _openMap;

    private readonly ModalOperator<MapSelectOperation, MapSystem> _selectModal;

    public float Priority => 0.85f;

    public MapMenu(MenuBarManager manager)
    {
        _context = manager.Context;
        _selectModal = new("SelectMap", () => new MapSelectOperation(_context), new Vector2(700, 0));
        _openMap = _context.Shortcuts.Register(
            "map.open",
            "Map",
            "Open Map",
            new KeyboardShortcut(ImGuiKey.M, ShortcutModifiers.Alt),
            Open);
    }

    public void Draw()
    {
        ImGuiEx.Menu("Map", () =>
        {
            ImGui.MenuItem(_context.Maps.Current.DisplayName, string.Empty, false, false);
            ImGui.Separator();

            if (ImGui.MenuItem("Open Map…", _openMap.ShortcutLabel))
            {
                Open();
            }
        });
    }

    // The picker is a popup, so it has to be opened and drawn at the root level rather than inside
    // the menu's ID stack — hence the overlay pass instead of drawing it from Draw().
    public void DrawOverlay()
    {
        MapSelectOperation? operation = _selectModal._item;
        if (_selectModal.Draw(_context.Maps, true, ImGuiWindowFlags.None) == ModalOperationState.Confirmed
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
