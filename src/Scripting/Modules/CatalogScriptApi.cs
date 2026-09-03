using System;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Queries over whatever catalog entities are currently loaded, exposed to JS as <c>wms.catalog</c>.
/// Catalog entities aren't streamed like scene entities — load the catalog through whatever system
/// owns it first (see <see cref="DatabaseSystem.LoadCatalog{TEntity}"/>). Create/delete aren't
/// exposed yet — see ScriptingPlan.md's open gap on this.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class CatalogScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "catalog";

    public float Priority => 0f;

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
}
