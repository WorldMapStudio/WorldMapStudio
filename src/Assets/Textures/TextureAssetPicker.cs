using System;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class TextureAssetPicker
{
    private readonly AssetSystem _assets;
    private readonly ModalDialogHost<TextureSelectionDialog, TextureSelectionContext> _modal =
        new("SelectTextureAsset", () => new TextureSelectionDialog(), new Vector2(720, 0));

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

        ModalDialogState state = _modal.Draw(_context, true, ImGuiWindowFlags.None);
        if (state is ModalDialogState.Confirmed or ModalDialogState.Cancelled)
        {
            _context = null;
        }
    }
}
