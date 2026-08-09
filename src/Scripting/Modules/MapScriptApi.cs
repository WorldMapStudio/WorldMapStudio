using System;
using System.Linq;

namespace WorldMapStudio;

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
    private readonly MapSystem _maps;

    public string Name => "map";

    public float Priority => 0f;

    public MapScriptApi(ScriptingSystem system)
    {
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
}
