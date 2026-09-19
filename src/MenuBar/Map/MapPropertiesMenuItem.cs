using ImGuiNET;

namespace WorldMapStudio;

/// <summary>"Map Properties…": opens the Map Properties window on the current map.</summary>
[Subsystem(nameof(MapMenu))]
public sealed class MapPropertiesMenuItem : IMenuItem
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _shortcut;

    public float Priority => 1f;

    public MapPropertiesMenuItem(MapMenu menu)
    {
        _context = menu.Context;
        _shortcut = _context.Shortcuts.Register(
            "map.properties",
            "Map",
            "Map Properties",
            KeyboardShortcut.None,
            Open);
    }

    public void Draw()
    {
        if (ImGui.MenuItem("Map Properties…", _shortcut.ShortcutLabel))
        {
            Open();
        }
    }

    // Built before WindowManager, so it is only read once invoked, never in the constructor.
    private void Open() => _context.WindowManager.MapPropertiesWindow.Open(_context.Maps.CurrentMap);
}
