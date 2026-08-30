using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

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

    /// <summary>
    /// Deletes a map, returning an error or null. Only the map itself goes: entities placed in it keep
    /// their rows (and their map id), so nothing is silently destroyed and re-creating the id restores
    /// them. Leaves the map the user is standing in only by moving them to another one.
    /// </summary>
    public string? Delete(Map map)
    {
        if (DeleteBlocker(map) is { } blocker)
        {
            return blocker;
        }

        IMapSource source = map.Source!;

        try
        {
            BlockingWork.Run(() => source.DeleteAsync(map));
        }
        catch (Exception e)
        {
            GD.PushError($"[Map] Failed to delete map {map.Id.Value}: {e.Message}");
            return e.Message;
        }

        _maps.Remove(map);
        Thumbnails.Remove(map.Id);

        if (Current.Id == map.Id)
        {
            Current = _maps[0];
        }

        Version++;
        return null;
    }

    /// <summary>Opens a map: streaming swaps to its entities and new entities land in it.</summary>
    public void Enter(Map map)
    {
        if (map.Id == Current.Id)
        {
            return;
        }

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
