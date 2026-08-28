using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Scene" menu: adds entities to the current scene. Adding an empty creates it at the origin,
/// records the creation in the active edit session (so it can be undone and is persisted on commit).
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class SceneMenu : IMainMenu
{
    private readonly EditorContext _context;
    private readonly ShortcutAction _addStamp;
    private readonly ShortcutAction _addDrawingTarget;
    private readonly ShortcutAction _deleteSelected;

    public float Priority => 0.75f;

    public SceneMenu(MenuBarManager manager)
    {
        _context = manager.Context;
        _addStamp = _context.Shortcuts.Register(
            "scene.add-landscape-stamp",
            "Scene",
            "Add Landscape Stamp",
            new KeyboardShortcut(ImGuiKey.S, ShortcutModifiers.Alt),
            AddStamp,
            () => _context.Landscape.IsEnabled);
        _addDrawingTarget = _context.Shortcuts.Register(
            "scene.add-drawing-target",
            "Scene",
            "Add Drawing Target",
            new KeyboardShortcut(ImGuiKey.D, ShortcutModifiers.Alt),
            AddDrawingTarget,
            () => _context.Landscape.IsEnabled);
        _deleteSelected = _context.Shortcuts.Register(
            "scene.delete-selected",
            "Scene",
            "Delete Selected",
            new KeyboardShortcut(ImGuiKey.Delete, ShortcutModifiers.None),
            DeleteSelected,
            () => _context.Selection.Selected.Count > 0);
    }

    public void Draw()
    {
        ImGuiEx.Menu("Scene", () =>
        {
            if (ImGui.BeginMenu("Add Empty"))
            {
                AddEmptyItem("Plain", MarkerShape.Plain);
                AddEmptyItem("Cube", MarkerShape.Cube);
                AddEmptyItem("Sphere", MarkerShape.Sphere);
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Add Landscape Stamp", _addStamp.ShortcutLabel, false, _context.Landscape.IsEnabled))
            {
                AddStamp();
            }

            if (ImGui.MenuItem("Add Drawing Target", _addDrawingTarget.ShortcutLabel, false, _context.Landscape.IsEnabled))
            {
                AddDrawingTarget();
            }

            ImGui.Separator();

            bool hasSelection = _context.Selection.Selected.Count > 0;
            if (ImGui.MenuItem("Delete Selected", _deleteSelected.ShortcutLabel, false, hasSelection))
            {
                DeleteSelected();
            }
        });
    }

    private void DeleteSelected()
    {
        EditSession session = _context.EditSessions.Active;
        foreach (SceneEntity entity in _context.Selection.Selected.OfType<SceneEntity>().ToList())
        {
            var command = new DeleteEntityCommand(_context.Scene, entity);
            command.Apply();
            session.Record(command);
            _context.Selection.Remove(entity);
        }
    }

    // Seeded from the catalog so a fresh stamp does something visible instead of needing four fields
    // filled in before it shows up at all.
    private void AddStamp()
    {
        LandscapeCatalog catalog = _context.Landscape.Catalog;
        var entity = new SceneEntity
        {
            Name = "Stamp",
            Map = _context.Maps.CurrentMap,
        };
        entity.AddComponent(new StampComponent
        {
            Channel = catalog.Channels.FirstOrDefault()?.Name ?? "",
        });
        entity.AddComponent(DefaultMaterialBind(catalog));

        _context.Scene.Add(entity);
        _context.Selection.Set(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
    }

    private void AddDrawingTarget()
    {
        LandscapeCatalog catalog = _context.Landscape.Catalog;
        var entity = new SceneEntity
        {
            Name = "Drawing Target",
            Map = _context.Maps.CurrentMap,
        };
        entity.AddComponent(new DrawingTargetComponent
        {
            Channel = catalog.Channels.FirstOrDefault()?.Name ?? "",
        });
        entity.AddComponent(DefaultMaterialBind(catalog));

        _context.Scene.Add(entity);
        _context.Selection.Set(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
    }

    private void AddEmptyItem(string label, MarkerShape shape)
    {
        if (!ImGui.MenuItem(label))
        {
            return;
        }

        var entity = new SceneEntity { Name = $"Empty ({shape})", Map = _context.Maps.CurrentMap };
        entity.AddComponent(new MarkerComponent { Shape = shape });
        _context.Scene.Add(entity);
        _context.Selection.Set(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
    }

    private static LandscapeMaterialBindComponent DefaultMaterialBind(LandscapeCatalog catalog)
    {
        var bind = new LandscapeMaterialBindComponent();
        bind.ReplaceBindings(
        [
            new LandscapeMaterialBinding(
                catalog.Layers.FirstOrDefault(layer => !layer.IsBase)?.RecordId,
                catalog.Materials.FirstOrDefault()?.RecordId),
        ]);
        return bind;
    }
}
