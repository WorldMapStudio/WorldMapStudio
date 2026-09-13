using System;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// A reusable "pick an entity from a catalog" search popup — the <see cref="ICatalogBrowser"/> analogue
/// of <c>ModelAssetPicker</c>, for any field that references another catalog's row by key (e.g.
/// <c>WowLightParams.LightSkyboxId</c> referencing the "Light Skybox" catalog). Deliberately separate
/// from <c>CatalogBrowserWindow</c> — a field editor wants a quick search-and-pick popup, not to
/// navigate the whole browser away from what it's currently showing, and more than one field across more
/// than one catalog is expected to want this same popup.
/// </summary>
public sealed class CatalogEntityPicker
{
    private readonly ModalOperator<CatalogEntitySelectionOperation, CatalogEntityPickerContext> _modal =
        new("SelectCatalogEntity", () => new CatalogEntitySelectionOperation(), new Vector2(460, 0));

    private CatalogEntityPickerContext? _context;

    /// <summary>Opens the popup against <paramref name="catalog"/>, starting from <paramref name="currentKey"/>
    /// (blank = none). <paramref name="select"/> is called once, with the chosen key or "" for Clear,
    /// when the popup is confirmed — never on Cancel.</summary>
    public void Browse(ICatalogBrowser catalog, string currentKey, Action<string> select)
    {
        _context = new CatalogEntityPickerContext(catalog, currentKey, select);
        _modal.Show();
    }

    /// <summary>Must be called every frame (even when no popup is open) for the popup to render —
    /// same contract as <c>ModelAssetPicker.Draw</c>.</summary>
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
