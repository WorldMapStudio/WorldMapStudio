using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Which <see cref="ICatalogSearchView"/>s exist for a catalog, and which one the user last picked for
/// it — the <see cref="CatalogReferenceLabels"/> analogue for views: reachable as
/// <see cref="EditorContext.CatalogSearchViews"/>, storage-agnostic like <see cref="Storage.CatalogSearchViews"/>
/// itself. One preference per <see cref="ICatalogBrowser.CatalogName"/>, shared by
/// <see cref="CatalogBrowserWindow"/> and every <see cref="CatalogEntityPicker"/> popup — switching the
/// view in one place is remembered everywhere that catalog is searched.
/// </summary>
public sealed class CatalogSearchViews
{
    private readonly EditorContext _context;

    // CatalogName -> ViewName. Persisted by CatalogBrowserWindow (the only ILayoutPersistentWindow that
    // needs it) through Snapshot/Restore, so this class itself stays free of JSON concerns.
    private readonly Dictionary<string, string> _preferred = new();

    public CatalogSearchViews(EditorContext context) => _context = context;

    private IEnumerable<ICatalogSearchView> AllViews =>
        _context.Database.Storages.SelectMany(storage => storage.CatalogSearchViews);

    /// <summary>Every view that supports <paramref name="catalog"/>, highest priority first. Always
    /// includes <see cref="CatalogListSearchView"/> — it supports every catalog.</summary>
    public IReadOnlyList<ICatalogSearchView> For(ICatalogBrowser catalog) =>
        AllViews.Where(view => view.Supports(catalog)).OrderByDescending(view => view.Priority).ToList();

    /// <summary>The remembered view for <paramref name="catalog"/> if it still exists and still supports
    /// it, otherwise the highest-priority one available.</summary>
    public ICatalogSearchView Preferred(ICatalogBrowser catalog)
    {
        IReadOnlyList<ICatalogSearchView> views = For(catalog);
        if (_preferred.TryGetValue(catalog.CatalogName, out string? viewName)
            && views.FirstOrDefault(view => view.ViewName == viewName) is { } remembered)
        {
            return remembered;
        }

        return views[0];
    }

    public void SetPreferred(ICatalogBrowser catalog, ICatalogSearchView view) =>
        _preferred[catalog.CatalogName] = view.ViewName;

    /// <summary>Every remembered preference, for <see cref="CatalogBrowserWindow.CaptureLayoutState"/> to
    /// serialize.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() => _preferred;

    /// <summary>Restores preferences from a previous <see cref="Snapshot"/> — a name this session no
    /// longer knows (a stale catalog or view name) is simply ignored by <see cref="Preferred"/> later,
    /// not rejected here.</summary>
    public void Restore(IReadOnlyDictionary<string, string> saved)
    {
        _preferred.Clear();
        foreach ((string catalogName, string viewName) in saved)
        {
            _preferred[catalogName] = viewName;
        }
    }
}
