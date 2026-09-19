using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Scripted access to the procedural model catalog, exposed to JS as <c>wms.procedural</c>. Search and
/// open exist because a script cannot otherwise reach a model the lazy catalog (see
/// <see cref="ProceduralModelFactory"/>) hasn't loaded — walking <see cref="ProceduralSystem.Models"/>
/// only ever sees the resident set, the same gap <see cref="CatalogScriptApi"/> already lives with for
/// every lazy catalog. Deliberately no loadAll(): a script that wants to walk every model pages
/// <see cref="Search"/>, the same thing a script walking a large lazily-loaded catalog already has to do.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ProceduralScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "procedural";

    public ProceduralScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Matches a page of models by id or a name substring, the same search the model picker's
    /// filter box runs. Returns "id\tlabel" lines — the id is what <see cref="Open"/> takes.</summary>
    [ScriptFunction]
    public async Task<string[]> Search(string filter)
    {
        IReadOnlyList<CatalogSearchResult> results = await _context.Procedural.ModelFactory.SearchAsync(filter).ConfigureAwait(false);
        return results.Select(result => $"{result.Key}\t{result.Label}").ToArray();
    }

    /// <summary>Loads and retains a model by id, adding it to the catalog registry if it isn't already
    /// there, and returns a handle to it. Null if the id doesn't exist.</summary>
    [ScriptFunction]
    public async Task<ScriptEntityHandle?> Open(int id)
    {
        CatalogEntity? entity = await _context.Procedural.ModelFactory.OpenAsync(_context, id.ToString()).ConfigureAwait(false);
        return entity is null ? null : new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity);
    }

    /// <summary>Every stored placement referencing this model, regardless of whether it is currently
    /// loaded — the count Delete safety uses (see <see cref="EditorStorage.ReferencingPlacementBoundsAsync"/>),
    /// not <see cref="ProceduralSystem.UsageCount"/>'s loaded-only one.</summary>
    [ScriptFunction]
    public async Task<int> UsageCount(int id)
    {
        EditorStorage storage = _context.Database.EditorStorage;
        var rows = await storage.ReferencingPlacementBoundsAsync(typeof(ProceduralModel), id).ConfigureAwait(false);
        return rows.Count;
    }
}
