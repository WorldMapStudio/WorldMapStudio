using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Scene" menu: adds entities to the current scene. Adding a scene entity creates it at the
/// origin; adding a prefab spawns it under the pointer. Both record the creation in the active edit
/// session (so it can be undone and is persisted on commit).
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class SceneMenu : IMainMenu
{
    private readonly EditorContext _context;
    private readonly PrefabPicker _prefabPicker;
    private readonly ShortcutAction _addSceneEntity;
    private readonly ShortcutAction _copySelected;
    private readonly ShortcutAction _paste;
    private readonly ShortcutAction _deleteSelected;

    public float Priority => 0.75f;

    public SceneMenu(MenuBarManager manager)
    {
        _context = manager.Context;
        _prefabPicker = new PrefabPicker(_context.Prefabs, _context.EditSessions, _context.Selection);
        _addSceneEntity = _context.Shortcuts.Register(
            "scene.add-entity",
            "Scene",
            "Add Scene Entity",
            new KeyboardShortcut(ImGuiKey.A, ShortcutModifiers.Alt),
            AddSceneEntity);
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
            () => _context.Selection.Selected.Count > 0 && _context.Tools.Active?.CapturesDelete != true);
    }

    public void Draw()
    {
        ImGuiEx.Menu("Scene", () =>
        {
            if (ImGui.MenuItem("Add Scene Entity", _addSceneEntity.ShortcutLabel))
            {
                AddSceneEntity();
            }

            if (ImGui.MenuItem("Add Prefab..."))
            {
                BrowsePrefabs();
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

        // Drawn unconditionally, like a component type's DrawModals(): the popup must keep rendering
        // every frame while open, independent of whether the "Scene" dropdown itself is open.
        _prefabPicker.Draw();
    }

    // Spawns at the pointer's world position when hovering the viewport, else the origin — same
    // fallback Paste() uses, since neither has a meaningful drop point without a hovered viewport.
    private void BrowsePrefabs()
    {
        ViewportPointer pointer = _context.Pointer;
        Vector3 at = pointer.Hovered && pointer.Valid ? pointer.WorldPoint : Vector3.Zero;
        _prefabPicker.Browse(at);
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

        // Shift the whole batch rigidly so the bottom-centre of its combined bounds lands under the
        // cursor, preserving whatever layout (and, for a parent/child pair, relative offset) the
        // copied entities had.
        Vector3 delta = pointer.WorldPoint - BoundsBottomCenter(pasted);
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

    // Horizontally centred, vertically at the lowest point: the natural anchor for dropping a batch
    // onto a surface, so pasted content sits on the ground under the cursor rather than being buried
    // or floating.
    private static Vector3 BoundsBottomCenter(IReadOnlyList<SceneEntity> entities)
    {
        Aabb bounds = entities[0].WorldBounds;
        for (int i = 1; i < entities.Count; i++)
        {
            bounds = bounds.Merge(entities[i].WorldBounds);
        }

        return new Vector3(bounds.Position.X + bounds.Size.X * 0.5f, bounds.Position.Y, bounds.Position.Z + bounds.Size.Z * 0.5f);
    }

    private void AddSceneEntity()
    {
        var entity = new SceneEntity { Name = "Entity", Map = _context.Maps.CurrentMap };
        _context.Scene.Add(entity);
        _context.Selection.Set(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
    }
}
