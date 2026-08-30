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

    private void AddSceneEntity()
    {
        var entity = new SceneEntity { Name = "Entity", Map = _context.Maps.CurrentMap };
        _context.Scene.Add(entity);
        _context.Selection.Set(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
    }
}
