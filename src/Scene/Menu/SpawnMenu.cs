using System.Linq;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// A "Spawn" menu entry per registered <see cref="ISpawnFactory"/> — "Add Creature..." today, a
/// gameobject spawn kind later, with no further change here: this class never names a spawn kind by
/// hand, only enumerates whichever factories <see cref="Storage.Spawners"/> reports. Its own top-level
/// menu rather than an item bolted onto <see cref="SceneMenu"/> (which already places prefabs the same
/// way), since this project's own convention is that a shared system should not explicitly reference
/// its members either way.
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class SpawnMenu : IMainMenu
{
    private readonly EditorContext _context;
    private readonly CatalogEntityPicker _picker = new();

    public float Priority => 0.76f;

    public SpawnMenu(MenuBarManager manager)
    {
        _context = manager.Context;
    }

    public void Draw()
    {
        ImGuiEx.Menu("Spawn", () =>
        {
            foreach (ISpawnFactory factory in _context.Database.Storages.SelectMany(storage => storage.Spawners))
            {
                if (ImGui.MenuItem($"Add {factory.SpawnKind}..."))
                {
                    Browse(factory);
                }
            }
        });

        // Drawn unconditionally, matching SceneMenu's own PrefabPicker.Draw() call: the popup must
        // keep rendering every frame while open, independent of whether the "Spawn" dropdown is open.
        _picker.Draw();
    }

    private void Browse(ISpawnFactory factory)
    {
        ICatalogBrowser? catalog = _context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .FirstOrDefault(c => c.CatalogName == factory.PickerCatalogName);
        if (catalog is null)
        {
            // No catalog of origin — this kind instances nothing (a taxi node, an area trigger), so
            // there is nothing to pick from. Create directly with no key instead of leaving the menu
            // item a dead no-op.
            Create(factory, "");
            return;
        }

        _picker.Browse(_context, catalog, "", key => Spawn(factory, key));
    }

    // Only reached from the catalog-picker path above, where "" means the picker's own Clear button —
    // a no-op, not a request to create with no template.
    private void Spawn(ISpawnFactory factory, string key)
    {
        if (key.Length == 0)
        {
            return;
        }

        Create(factory, key);
    }

    // Placed at the pointer's world position when hovering the viewport — already ground-snapped by
    // ViewportPointer's own terrain raycast (ViewportWindow.UpdatePointer) — else the origin, the exact
    // fallback SceneMenu.BrowsePrefabs uses for the same reason: neither has a meaningful drop point
    // without a hovered viewport.
    private void Create(ISpawnFactory factory, string key)
    {
        ViewportPointer pointer = _context.Pointer;
        Vector3 at = pointer.Hovered && pointer.Valid ? pointer.WorldPoint : Vector3.Zero;
        var transform = new Transform3D(Basis.Identity, at);

        SceneEntity entity = factory.Create(_context, _context.Maps.CurrentMap, transform, key);
        _context.Selection.Set(entity);
    }
}
