using System;
using ImGuiNET;
using Godot;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class ModelAssetPicker
{
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly Node _previewOwner;
    private readonly ModalDialogHost<ModelSelectionDialog, ModelSelectionContext> _modal =
        new("SelectModelAsset", () => new ModelSelectionDialog(), new Vector2(900, 0));

    private ModelSelectionContext? _context;

    public ModelAssetPicker(AssetSystem assets, MeshMaterialSystem materials, Node previewOwner)
    {
        _assets = assets;
        _materials = materials;
        _previewOwner = previewOwner;
    }

    public void Browse(string currentPath, Action<string> select)
    {
        _context = new ModelSelectionContext(_assets, _materials, _previewOwner, currentPath, select);
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
}
