using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>One row of what a map delete would remove, as JS sees it — see
/// <see cref="MapContentsDescriptor"/>.</summary>
public sealed class MapContentRow
{
    internal MapContentRow(string label, int count)
    {
        Label = label;
        Count = count;
    }

    [ScriptProperty]
    public string Label { get; }

    [ScriptProperty]
    public int Count { get; }
}

/// <summary>What deleting a map would remove, as JS sees it — see <see cref="MapScriptApi.DescribeContents"/>.</summary>
public sealed class MapContentsDescriptor
{
    internal MapContentsDescriptor(MapContents contents)
    {
        Data = contents.Data.Select(row => new MapContentRow(row.Label, row.Count)).ToArray();
        MapOnlyResources = contents.MapOnlyResources.Select(row => new MapContentRow(row.Label, row.Count)).ToArray();
    }

    [ScriptProperty]
    public MapContentRow[] Data { get; }

    [ScriptProperty]
    public MapContentRow[] MapOnlyResources { get; }
}

/// <summary>
/// A read-only snapshot of a map's id/name, safe to hand to JS. <see cref="Map"/> itself carries no
/// [Script*] attributes (it's a plain internal model, not part of the curated surface), so this is
/// the boundary type instead — the same reason entities cross as <see cref="ScriptEntityHandle"/>
/// rather than raw references, just without the live-resolution concern since a map, unlike an
/// entity, is never streamed out from under a script.
/// </summary>
public sealed class MapDescriptor
{
    public MapDescriptor(Map map)
    {
        Id = map.Id.Value;
        Name = map.Name;
    }

    [ScriptProperty]
    public int Id { get; }

    [ScriptProperty]
    public string Name { get; }
}

/// <summary>Lists, opens and creates maps, exposed to JS as <c>wms.map</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class MapScriptApi : IScriptModule
{
    private readonly EditorContext _context;
    private readonly MapSystem _maps;

    public string Name => "map";

    public float Priority => 0f;

    public MapScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
        _maps = system.Context.Maps;
    }

    [ScriptProperty]
    public MapDescriptor Current => new(_maps.Current);

    [ScriptFunction]
    public MapDescriptor[] List() => _maps.Maps.Select(map => new MapDescriptor(map)).ToArray();

    /// <summary>Opens a map: streaming swaps to its entities and new entities land in it.</summary>
    [ScriptFunction]
    public void Open(int id)
    {
        Map map = _maps.Maps.FirstOrDefault(m => m.Id.Value == id)
            ?? throw new InvalidOperationException($"No map with id {id}.");
        _maps.Enter(map);
    }

    /// <summary>Creates a map in the first storage that accepts new ones, and opens it — same as the "New Map" picker flow.</summary>
    [ScriptFunction]
    public MapDescriptor Create(int id, string name)
    {
        Map? created = _maps.Create(id, name, out string? error);
        if (created is null)
        {
            throw new InvalidOperationException(error ?? $"Failed to create map {id}.");
        }

        _maps.Enter(created);
        return new MapDescriptor(created);
    }

    /// <summary>Renames a map — missing until now, and trivial next to <see cref="Delete"/>.</summary>
    [ScriptFunction]
    public void Rename(int id, string name)
    {
        if (_maps.Rename(Find(id), name) is { } error)
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Opens the Map Properties window on a map — UI driving, so a script can bring the same
    /// window a user would see to the front. Looked up lazily rather than cached at construction: this
    /// module is built before <see cref="MenuBarManager"/> exists — see <see cref="EditorContext"/>'s
    /// own constructor order.</summary>
    [ScriptFunction]
    public void ShowProperties(int id) =>
        _context.MenuBarManager.WindowManager.Windows.OfType<MapPropertiesWindow>().FirstOrDefault()?.Open(Find(id).Id);

    /// <summary>
    /// Deletes a map, its own contents (by default), and — for each named resource kind — the
    /// resources only it used. <paramref name="deleteResources"/> takes resource labels or type names,
    /// e.g. <c>["Images", "Procedural models"]</c>. Resolves once the delete finishes; throws with the
    /// blocker or error text if it couldn't run or didn't succeed.
    /// </summary>
    [ScriptFunction]
    public async Task Delete(int id, bool deleteContents = true, string[]? deleteResources = null)
    {
        var options = new MapDeleteOptions(deleteContents, ResolveResourceTypes(deleteResources));
        WorkHandle? handle = _maps.Delete(Find(id), options, out string? error);
        if (handle == null)
        {
            throw new InvalidOperationException(error ?? $"Could not delete map {id}.");
        }

        await WaitAsync(handle).ConfigureAwait(false);
    }

    /// <summary>What deleting a map would remove — the counts the delete popup itself shows.</summary>
    [ScriptFunction]
    public async Task<MapContentsDescriptor> DescribeContents(int id) =>
        new(await _maps.DescribeContentsAsync(new MapId(id)).ConfigureAwait(false));

    /// <summary>Ids with data left behind by a map deleted before this cleanup existed — no
    /// <c>wms_maps</c> row, but rows in some <see cref="IMapScopedData"/> owner all the same.</summary>
    [ScriptFunction]
    public async Task<int[]> StrayMapIds() => (await _maps.FindStrayMapIdsAsync().ConfigureAwait(false)).ToArray();

    /// <summary>Purges one id found by <see cref="StrayMapIds"/> — the same delete <see cref="Delete"/>
    /// runs, without the map-row step a stray id has none of.</summary>
    [ScriptFunction]
    public async Task PurgeStrayMap(int id, string[]? deleteResources = null)
    {
        var options = new MapDeleteOptions(true, ResolveResourceTypes(deleteResources));
        WorkHandle? handle = _maps.PurgeStrayMap(id, options, out string? error);
        if (handle == null)
        {
            throw new InvalidOperationException(error ?? $"Could not purge map {id}.");
        }

        await WaitAsync(handle).ConfigureAwait(false);
    }

    private Map Find(int id) =>
        _maps.Maps.FirstOrDefault(map => map.Id.Value == id)
        ?? throw new InvalidOperationException($"No map with id {id}.");

    /// <summary>Matches each name against a registered <see cref="IMapOwnableResourceFactory"/>'s label
    /// or type name, case-insensitively. Throws naming the valid options on a miss, since a typo here
    /// would otherwise silently delete nothing.</summary>
    private HashSet<Type> ResolveResourceTypes(string[]? labelsOrTypeNames)
    {
        var result = new HashSet<Type>();
        if (labelsOrTypeNames is not { Length: > 0 })
        {
            return result;
        }

        IReadOnlyList<(Type ResourceType, string Label)> kinds = _maps.ResourceKinds();
        foreach (string name in labelsOrTypeNames)
        {
            if (kinds.FirstOrDefault(kind =>
                    string.Equals(kind.Label, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind.ResourceType.Name, name, StringComparison.OrdinalIgnoreCase))
                is not { ResourceType: { } resourceType })
            {
                throw new InvalidOperationException(
                    $"No map-ownable resource kind named '{name}'. Valid names: {string.Join(", ", kinds.Select(kind => kind.Label))}.");
            }

            result.Add(resourceType);
        }

        return result;
    }

    /// <summary>Polls until a scheduled delete/purge finishes, then throws if it faulted — the same
    /// "await the work" contract <see cref="BatchScriptApi.Wait"/> gives a batch run, but unbounded:
    /// a map delete has no host-imposed settlement deadline to respect.</summary>
    private static async Task WaitAsync(WorkHandle handle)
    {
        while (handle.State is WorkState.Queued or WorkState.Executing)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        if (handle.State == WorkState.Faulted)
        {
            throw new InvalidOperationException(handle.Snapshot().Error);
        }
    }
}
