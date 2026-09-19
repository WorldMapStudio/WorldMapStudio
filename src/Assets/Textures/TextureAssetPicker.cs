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

    /// <summary>Opens the picker. <paramref name="filter"/> pre-fills its filter box.</summary>
    public void Browse(string currentPath, Action<string> select, string filter = "")
    {
        _context = new TextureSelectionContext(_assets, currentPath, select, filter);
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
