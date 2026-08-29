using System.Collections.Generic;
using System.Linq;
using Godot;
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
    private readonly ShortcutAction _addRoad;
    private readonly ShortcutAction _copySelected;
    private readonly ShortcutAction _paste;
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
        _addRoad = _context.Shortcuts.Register(
            "scene.add-road",
            "Scene",
            "Add Road",
            new KeyboardShortcut(ImGuiKey.R, ShortcutModifiers.Alt),
            AddRoad,
            () => _context.Landscape.IsEnabled);
        _copySelected = _context.Shortcuts.Register(
            "scene.copy-selected",
            "Scene",
            "Copy",
            new KeyboardShortcut(ImGuiKey.C, ShortcutModifiers.Ctrl),
            CopySelected,
            () => _context.Selection.Selected.OfType<SceneEntity>().Any());
        _paste = _context.Shortcuts.Register(
            "scene.paste",
            "Scene",
            "Paste",
            new KeyboardShortcut(ImGuiKey.V, ShortcutModifiers.Ctrl),
            Paste,
            () => _context.Clipboard.HasContent);
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

            if (ImGui.MenuItem("Add Road", _addRoad.ShortcutLabel, false, _context.Landscape.IsEnabled))
            {
                AddRoad();
            }

            ImGui.Separator();

            bool hasSceneSelection = _context.Selection.Selected.OfType<SceneEntity>().Any();
            if (ImGui.MenuItem("Copy", _copySelected.ShortcutLabel, false, hasSceneSelection))
            {
                CopySelected();
            }

            if (ImGui.MenuItem("Paste", _paste.ShortcutLabel, false, _context.Clipboard.HasContent))
            {
                Paste();
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
            // Derived entities (e.g. landscape chunks) are computed, not authored, so there's
            // nothing to delete — leave them selected rather than throwing out of the loop.
            if (entity is IDerivedEntity)
            {
                continue;
            }

            var command = new DeleteEntityCommand(_context.Scene, entity);
            command.Apply();
            session.Record(command);
            _context.Selection.Remove(entity);
        }
    }

    // Clones the current selection into the clipboard immediately: the clipboard owns independent
    // copies from this point on, so it keeps working even if the source entities later stream out or
    // get deleted before the user pastes.
    private void CopySelected() =>
        _context.Clipboard.Copy(_context.Selection.Selected.OfType<SceneEntity>());

    // Pastes wherever the mouse currently projects into the world rather than on top of the copied
    // entities: not over the viewport means no meaningful drop point, so it's a no-op there.
    private void Paste()
    {
        ViewportPointer pointer = _context.Pointer;
        if (!pointer.Hovered || !pointer.Valid)
        {
            return;
        }

        IReadOnlyList<SceneEntity> pasted = _context.Clipboard.Paste(_context.Maps.CurrentMap);
        if (pasted.Count == 0)
        {
            return;
        }

        // Shift the whole batch rigidly so its centre lands under the cursor, preserving whatever
        // layout (and, for a parent/child pair, relative offset) the copied entities had.
        Vector3 delta = pointer.WorldPoint - Centroid(pasted);
        foreach (SceneEntity entity in pasted)
        {
            Transform3D transform = entity.Transform;
            entity.Transform = new Transform3D(transform.Basis, transform.Origin + delta);
        }

        var commands = new List<IEditCommand>();
        _context.Selection.Clear();
        foreach (SceneEntity entity in pasted)
        {
            _context.Scene.Add(entity);
            _context.Selection.Add(entity);
            commands.Add(new CreateEntityCommand(_context.Scene, entity));
        }

        _context.EditSessions.Record(commands.Count == 1
            ? commands[0]
            : new BatchEditCommand($"Paste {commands.Count} entities", commands));
    }

    private static Vector3 Centroid(IReadOnlyList<SceneEntity> entities)
    {
        Vector3 sum = Vector3.Zero;
        foreach (SceneEntity entity in entities)
        {
            sum += entity.Transform.Origin;
        }

        return sum / entities.Count;
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

    // Seeded from the catalog, and with a short starter edge, so a fresh road paints something
    // visible instead of needing six fields filled in before it shows up at all.
    private void AddRoad()
    {
        LandscapeCatalog catalog = _context.Landscape.Catalog;

        var road = new RoadComponent
        {
            CentreChannel = catalog.Channels.FirstOrDefault()?.Name ?? "",
        };
        road.ShoulderChannel = catalog.Channels.Skip(1).FirstOrDefault()?.Name ?? road.CentreChannel;

        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-5.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(5.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        road.ReplaceNetwork(network);

        var entity = new SceneEntity
        {
            Name = "Road",
            Map = _context.Maps.CurrentMap,
        };
        entity.AddComponent(road);

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
