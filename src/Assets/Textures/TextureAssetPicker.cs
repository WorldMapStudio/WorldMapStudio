using System;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class TextureAssetPicker
{
    private readonly AssetSystem _assets;
    private readonly ModalOperator<TextureSelectionOperation, TextureSelectionContext> _modal =
        new("SelectTextureAsset", () => new TextureSelectionOperation(), new Vector2(720, 0));

    private TextureSelectionContext? _context;

    public TextureAssetPicker(AssetSystem assets)
    {
        _assets = assets;
    }

    public void Browse(string currentPath, Action<string> select)
    {
        _context = new TextureSelectionContext(_assets, currentPath, select);
        _modal.Show();
    }

    public void Draw()
    {
        if (_context == null)
        {
            return;
        }

        ModalOperationState state = _modal.Draw(_context, true, ImGuiWindowFlags.None);
        if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
        {
            _context = null;
        }
    }
}
