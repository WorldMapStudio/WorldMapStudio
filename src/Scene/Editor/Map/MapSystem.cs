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
public sealed class MapSystem
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
                foreach (Map map in Run(source.LoadAsync))
                {
                    // First source to claim an id wins; ids are what entities store, so they can't collide.
                    if (_maps.All(existing => existing.Id != map.Id))
                    {
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

        if (Sources.FirstOrDefault(source => source.CanCreate) is not { } source)
        {
            error = "No storage accepts new maps.";
            return null;
        }

        string trimmed = name.Trim();
        var created = new Map(new MapId(id), trimmed.Length == 0 ? $"Map {id}" : trimmed);

        try
        {
            Run(() => source.CreateAsync(created));
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

    // Blocking DB work must run off the Godot main thread's synchronization context, or a
    // continuation deadlocks trying to resume on the thread we are blocking (see MigrationSystem).
    private static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    private static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
}
