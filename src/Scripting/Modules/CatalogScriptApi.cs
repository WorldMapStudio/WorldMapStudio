using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Queries over whatever catalog entities are currently loaded, exposed to JS as <c>wms.catalog</c>.
/// Catalog entities aren't streamed like scene entities — load the catalog through whatever system
/// owns it first (see <see cref="DatabaseSystem.LoadCatalog{TEntity}"/>). Create/delete aren't
/// exposed yet.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class CatalogScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "catalog";

    public CatalogScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Loaded catalog entities of one CLR type by name (e.g. "Quest").</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] All(string typeName)
    {
        ScriptEntityHandle[] handles = _context.Catalog.Entities.Where(entity => entity.GetType().Name == typeName)
            .Select(entity => new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity))
            .ToArray();

        if (handles.Length == 0 && !KnownTypeNames().Contains(typeName))
        {
            throw new InvalidOperationException(
                $"No catalog entity type named '{typeName}'. Known: {string.Join(", ", KnownTypeNames().OrderBy(name => name))}.");
        }

        return handles;
    }

    private string[] KnownTypeNames() =>
        _context.Database.Storages
            .SelectMany(storage => storage.CatalogFactories.Select(f => f.EntityType)
                .Concat(storage.LazyCatalogFactories.Select(f => f.EntityType)))
            .Select(type => type.Name)
            .Distinct()
            .ToArray();

    /// <summary>Every <see cref="ICatalogSearchView"/> available for a catalog (by
    /// <see cref="ICatalogBrowser.CatalogName"/>), highest priority first — what
    /// <see cref="SetView"/> accepts.</summary>
    [ScriptFunction]
    public string[] Views(string catalogName) =>
        _context.CatalogSearchViews.For(Browser(catalogName)).Select(view => view.ViewName).ToArray();

    /// <summary>Switches a catalog's search view — the same preference
    /// <c>CatalogBrowserWindow</c>'s own view toggle sets, shared with every Load popup for that
    /// catalog. Lets a script (and so an LLM) drive the UI toward a gallery view exactly as a user
    /// clicking the toggle would.</summary>
    [ScriptFunction]
    public void SetView(string catalogName, string viewName)
    {
        ICatalogBrowser catalog = Browser(catalogName);
        IReadOnlyList<ICatalogSearchView> views = _context.CatalogSearchViews.For(catalog);
        ICatalogSearchView view = views.FirstOrDefault(candidate => candidate.ViewName == viewName)
            ?? throw new InvalidOperationException(
                $"No view named '{viewName}' for catalog '{catalogName}'. Known: {string.Join(", ", views.Select(v => v.ViewName))}.");

        _context.CatalogSearchViews.SetPreferred(catalog, view);
    }

    private ICatalogBrowser Browser(string catalogName) =>
        _context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .FirstOrDefault(browser => browser.CatalogName == catalogName)
            ?? throw new InvalidOperationException($"No catalog named '{catalogName}'.");
}
