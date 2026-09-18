using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore.Storage;

namespace WorldMapStudio;

/// <summary>What to remove of a map's own contents and the resources only it uses — see
/// <see cref="MapSystem.Delete"/>.</summary>
public sealed record MapDeleteOptions(bool DeleteContents, IReadOnlySet<Type> DeleteResources)
{
    /// <summary>Everything a map owns, no shared resources — the default a fresh delete popup opens on.</summary>
    public static MapDeleteOptions ContentsOnly { get; } = new(true, new HashSet<Type>());
}

/// <summary>What a map delete would remove, for the confirmation popup — see
/// <see cref="MapSystem.DescribeContentsAsync"/>.</summary>
public sealed record MapContents(
    IReadOnlyList<(string Label, int Count)> Data,
    IReadOnlyList<(Type ResourceType, string Label, int Count)> MapOnlyResources);

/// <summary>
/// Owns the editor's set of maps and which one is open. Maps are gathered from the
/// <see cref="IMapSource"/>s registered into the storages, so plugins can contribute their own.
/// Exactly one map is open at a time: streaming loads that map's entities and newly created entities
/// are placed in it, while entities from other maps stay loaded for as long as the edit session pins
/// them (see <see cref="StreamingSystem"/>).
/// </summary>
public sealed class MapSystem : IWorldParticipant
{
    private readonly EditorContext _context;
    private readonly List<Map> _maps = [];

    public MapSystem(EditorContext context)
    {
        _context = context;
        Thumbnails = new MapThumbnails(ProjectStore.ProjectFolder(context.Project.Name));
    }

    /// <summary>Every known map, ordered by id.</summary>
    public IReadOnlyList<Map> Maps => _maps;

    /// <summary>The map being viewed and edited. Never null: a project always has a map to work in.</summary>
    public Map Current { get; private set; } = new(new MapId(0), "Default");

    public MapId CurrentMap => Current.Id;

    /// <summary>Bumps whenever the map list or the current map changes, so views can tell when to refresh.</summary>
    public int Version { get; private set; }

    /// <summary>Per-map preview images for the map picker.</summary>
    public MapThumbnails Thumbnails { get; }

    /// <summary>The last load error, shown in the picker; null when the maps loaded cleanly.</summary>
    public string? Error { get; private set; }

    /// <summary>Grabs the live 3D view as an image. Set by the viewport; used for map previews.</summary>
    public Func<Image?>? CaptureView { get; set; }

    private IEnumerable<IMapSource> Sources =>
        _context.Database.Storages.SelectMany(storage => storage.MapSources);

    /// <summary>
    /// (Re)reads the maps from every source. Called when the editor opens — that is, after the
    /// migration gate, so the <c>maps</c> table is guaranteed to exist by then.
    /// </summary>
    public void Load()
    {
        _maps.Clear();
        Error = null;

        foreach (IMapSource source in Sources)
        {
            try
            {
                foreach (Map map in BlockingWork.Run(source.LoadAsync))
                {
                    // First source to claim an id wins; ids are what entities store, so they can't collide.
                    if (_maps.All(existing => existing.Id != map.Id))
                    {
                        map.Source = source;
                        _maps.Add(map);
                    }
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                GD.PushError($"[Map] Failed to load maps: {e.Message}");
            }
        }

        // Entities default to map 0, so a project with no maps at all still needs one to work in.
        if (_maps.Count == 0 && Error == null)
        {
            Create(0, "Default", out _);
        }

        Sort();
        Current = _maps.FirstOrDefault(map => map.Id == Current.Id) ?? _maps.FirstOrDefault() ?? Current;
        Version++;
    }

    // Maps have to be the first thing loaded (and the last thing unloaded): the current map id is
    // what LandscapeSystem's own load reads.
    string? IWorldParticipant.LoadStep => "Loading maps";

    void IWorldParticipant.LoadWorld() => Load();

    void IWorldParticipant.UnloadWorld()
    {
        _maps.Clear();
        Current = new Map(new MapId(0), "Default");
        Error = null;
        Version++;
    }

    /// <summary>
    /// Creates a map in the first source that accepts new ones, returning it, or null with
    /// <paramref name="error"/> set.
    /// </summary>
    public Map? Create(int id, string name, out string? error)
    {
        if (_maps.Any(map => map.Id.Value == id))
        {
            error = $"A map with id {id} already exists.";
            return null;
        }

        if (Sources.FirstOrDefault(source => source.CanEdit) is not { } source)
        {
            error = "No storage accepts new maps.";
            return null;
        }

        string trimmed = name.Trim();
        var created = new Map(new MapId(id), trimmed.Length == 0 ? $"Map {id}" : trimmed) { Source = source };

        try
        {
            BlockingWork.Run(() => source.CreateAsync(created));
        }
        catch (Exception e)
        {
            GD.PushError($"[Map] Failed to create map {id}: {e.Message}");
            error = e.Message;
            return null;
        }

        _maps.Add(created);
        Sort();
        Version++;
        error = null;
        GD.Print($"[Map] Created map {id} '{created.Name}'.");
        return created;
    }

    /// <summary>
    /// Renames a map, returning an error or null. Safe at any time: entities reference a map by
    /// <see cref="Map.Id"/>, so the name is nothing but a label.
    /// </summary>
    public string? Rename(Map map, string name)
    {
        IMapSource? source = map.Source;
        if (source is not { CanEdit: true })
        {
            return "This map comes from a read-only source.";
        }

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "A map needs a name.";
        }

        if (trimmed == map.Name)
        {
            return null;
        }

        string previous = map.Name;
        map.Name = trimmed;

        try
        {
            BlockingWork.Run(() => source.RenameAsync(map));
        }
        catch (Exception e)
        {
            map.Name = previous;
            GD.PushError($"[Map] Failed to rename map {map.Id.Value}: {e.Message}");
            return e.Message;
        }

        Version++;
        return null;
    }

    /// <summary>
    /// Why <paramref name="map"/> can't be deleted right now, or null when it can. Deleting is barred
    /// while an edit session is open: the session pins entities that may live in the map (and holds
    /// undo commands that would revert into it), so removing it underneath them invites nonsense.
    /// </summary>
    public string? DeleteBlocker(Map map)
    {
        if (map.Source is not { CanEdit: true })
        {
            return "This map comes from a read-only source.";
        }

        if (_maps.Count <= 1)
        {
            return "The project must keep at least one map.";
        }

        if (_context.Operations.Blocker is { } blocker)
        {
            return blocker;
        }

        return null;
    }

    /// <summary>The "Editor" storage — the only one <see cref="IMapScopedData"/> and
    /// <see cref="IMapOwnableResourceFactory"/> ever register into. Null only if a project somehow has
    /// none, in which case a delete removes just the map row, as it always used to.</summary>
    private EditorStorage? EditorContentsStorage => _context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();

    /// <summary>
    /// What deleting <paramref name="map"/> would remove: one row per non-empty <see cref="IMapScopedData"/>
    /// owner, and one row per resource kind referenced only by this map. Read-only — computing the
    /// resource rows still requires no write, since nothing is deleted here.
    /// </summary>
    public async Task<MapContents> DescribeContentsAsync(MapId map)
    {
        if (EditorContentsStorage is not { } storage)
        {
            return new MapContents([], []);
        }

        using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = storage.CreateContext();

        var data = new List<(string Label, int Count)>();
        foreach (IMapScopedData owner in storage.MapScopedData)
        {
            int count = await owner.CountAsync(context, map).ConfigureAwait(false);
            if (count > 0)
            {
                data.Add((owner.Label, count));
            }
        }

        var resources = new List<(Type ResourceType, string Label, int Count)>();
        IReadOnlyDictionary<Type, IReadOnlyList<int>> mapOnly = await storage.FindMapOnlyResourcesAsync(context, map).ConfigureAwait(false);
        foreach (IMapOwnableResourceFactory factory in storage.MapOwnableResourceFactories)
        {
            if (mapOnly.TryGetValue(factory.ResourceType, out IReadOnlyList<int>? ids) && ids.Count > 0)
            {
                resources.Add((factory.ResourceType, factory.Label, ids.Count));
            }
        }

        return new MapContents(data, resources);
    }

    /// <summary>
    /// Deletes a map, its own contents, and — for each ticked resource kind — the resources only it
    /// used. Returns null with <paramref name="error"/> set, and schedules nothing, if the map can't be
    /// deleted right now (see <see cref="DeleteBlocker"/>); otherwise runs as an exclusive
    /// <see cref="WorldOperations"/> operation, since it rewrites the database behind the loaded
    /// world's back. The reload that follows re-reads the map list, which is what actually drops the
    /// deleted map from <see cref="Maps"/> and moves <see cref="Current"/> off it if needed — nothing
    /// here mutates either directly.
    /// </summary>
    public WorkHandle? Delete(Map map, MapDeleteOptions options, out string? error)
    {
        if (DeleteBlocker(map) is { } blocker)
        {
            error = blocker;
            return null;
        }

        IMapSource source = map.Source!;
        string mapLabel = $"'{map.DisplayName}' (id {map.Id.Value})";

        WorkHandle? handle = _context.Operations.TryRun($"Delete map {mapLabel}", async ctx =>
        {
            if (options.DeleteContents && EditorContentsStorage is { } storage)
            {
                await DeleteContentsAsync(storage, map.Id, options.DeleteResources).ConfigureAwait(false);
            }

            await source.DeleteAsync(map).ConfigureAwait(false);
            GD.Print($"[Map] Deleted map {mapLabel}.");

            // Dictionary-backed and read by the picker's draw loop on the main thread — dropped there
            // rather than from this background work to avoid racing that read.
            await ctx.SwitchToMain();
            Thumbnails.Remove(map.Id);
        }, out error);

        return handle;
    }

    /// <summary>
    /// One transaction: compute the map-only resource ids for the ticked kinds first (their references
    /// disappear the moment the entities do), then run every <see cref="IMapScopedData"/> owner in
    /// priority order, then delete the ticked resources. See <see cref="IMapScopedData"/> for why the
    /// order among owners matters and this one doesn't relative to them.
    /// </summary>
    private static async Task DeleteContentsAsync(EditorStorage storage, MapId map, IReadOnlySet<Type> deleteResources)
    {
        await storage.CommitTransactionAsync(async context =>
        {
            DbTransaction transaction = context.Database.CurrentTransaction!.GetDbTransaction();

            Dictionary<Type, IReadOnlyList<int>> resourceIds = [];
            if (deleteResources.Count > 0)
            {
                IReadOnlyDictionary<Type, IReadOnlyList<int>> mapOnly = await storage.FindMapOnlyResourcesAsync(context, map).ConfigureAwait(false);
                foreach (Type type in deleteResources)
                {
                    if (mapOnly.TryGetValue(type, out IReadOnlyList<int>? ids))
                    {
                        resourceIds[type] = ids;
                    }
                }
            }

            foreach (IMapScopedData owner in storage.MapScopedData)
            {
                int count = await owner.CountAsync(context, map).ConfigureAwait(false);
                await owner.DeleteAsync(context, transaction, map).ConfigureAwait(false);
                if (count > 0)
                {
                    GD.Print($"[Map] Deleted {count} {owner.Label} from map {map.Value}.");
                }
            }

            foreach (IMapOwnableResourceFactory factory in storage.MapOwnableResourceFactories)
            {
                if (!resourceIds.TryGetValue(factory.ResourceType, out IReadOnlyList<int>? ids) || ids.Count == 0)
                {
                    continue;
                }

                await factory.DeleteAsync(context, transaction, ids).ConfigureAwait(false);
                GD.Print($"[Map] Deleted {ids.Count} {factory.Label} used only by map {map.Value}.");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Every map id an <see cref="IMapScopedData"/> owner holds rows for but that isn't a known map
    /// (nor the prefab library) — data left behind by a map deleted before this cleanup existed. See
    /// §6 of the delete-map plan.
    /// </summary>
    public async Task<IReadOnlyList<int>> FindStrayMapIdsAsync()
    {
        if (EditorContentsStorage is not { } storage)
        {
            return [];
        }

        using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = storage.CreateContext();

        var found = new HashSet<int>();
        foreach (IMapScopedData owner in storage.MapScopedData)
        {
            found.UnionWith(await owner.MapIdsAsync(context).ConfigureAwait(false));
        }

        found.ExceptWith(_maps.Select(map => map.Id.Value));
        found.Remove(PrefabSystem.LibraryMap.Value);
        return found.OrderBy(id => id).ToList();
    }

    /// <summary>Purges a stray id's data the same way <see cref="Delete"/> purges a real map's — without
    /// the map-row step, since a stray id has none. See <see cref="FindStrayMapIdsAsync"/>.</summary>
    public WorkHandle? PurgeStrayMap(int id, MapDeleteOptions options, out string? error)
    {
        if (_context.Operations.Blocker is { } blocker)
        {
            error = blocker;
            return null;
        }

        if (EditorContentsStorage is not { } storage)
        {
            error = "No storage hosts map-scoped data.";
            return null;
        }

        var map = new MapId(id);
        return _context.Operations.TryRun($"Clean up map {id}", async _ =>
        {
            if (options.DeleteContents)
            {
                await DeleteContentsAsync(storage, map, options.DeleteResources).ConfigureAwait(false);
            }

            GD.Print($"[Map] Cleaned up stray data for map {id}.");
        }, out error);
    }

    /// <summary>Opens a map: streaming swaps to its entities and new entities land in it.</summary>
    public void Enter(Map map)
    {
        if (map.Id == Current.Id)
        {
            return;
        }

        GD.Print($"[Map] Entering map {map.Id.Value} '{map.DisplayName}'.");
        Current = map;
        Version++;
    }

    /// <summary>The lowest id no map is using yet, as the default for a newly created map.</summary>
    public int NextFreeId()
    {
        int id = 0;
        while (_maps.Any(map => map.Id.Value == id))
        {
            id++;
        }

        return id;
    }

    /// <summary>Snapshots the live 3D view as a map's preview image. Main thread only.</summary>
    public void CaptureThumbnail(MapId map)
    {
        if (CaptureView?.Invoke() is { } image)
        {
            Thumbnails.Save(map, image);
        }
    }

    private void Sort() => _maps.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
}
