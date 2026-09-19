using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>Spawns a saved <see cref="Prefab"/> into the scene, or deletes one (searchable popup,
/// modelled on <see cref="ModelAssetPicker"/>). Owned by <see cref="SceneMenu"/>.</summary>
public sealed class PrefabPicker
{
    private readonly PrefabSystem _prefabs;
    private readonly SelectionSystem _selection;
    private readonly ModalDialogHost<PrefabSelectionDialog, PrefabSelectionContext> _modal =
        new("SelectPrefab", () => new PrefabSelectionDialog(), new Vector2(460, 0));

    private PrefabSelectionContext? _context;
    private Vector3 _spawnAt;

    public PrefabPicker(PrefabSystem prefabs, SelectionSystem selection)
    {
        _prefabs = prefabs;
        _selection = selection;
    }

    public void Browse(Vector3 spawnAt)
    {
        _spawnAt = spawnAt;
        _context = new PrefabSelectionContext(_prefabs.All.ToList(), Spawn, Delete);
        _modal.Show();
    }

    public void Draw()
    {
        if (_context == null)
        {
            return;
        }

        ModalDialogState state = _modal.Draw(_context, true, ImGuiWindowFlags.None);
        if (state is ModalDialogState.Confirmed or ModalDialogState.Cancelled)
        {
            _context = null;
        }
    }

    private void Spawn(Prefab prefab)
    {
        IReadOnlyList<SceneEntity> entities = _prefabs.Spawn(prefab, _spawnAt);

        _selection.Clear();
        foreach (SceneEntity entity in entities)
        {
            _selection.Add(entity);
        }
    }

    private void Delete(Prefab prefab)
    {
        _prefabs.Delete(prefab);

        // Refresh the list the popup is drawing from so the deleted prefab disappears immediately.
        _context = _context! with { Prefabs = _prefabs.All.ToList() };
    }
}
