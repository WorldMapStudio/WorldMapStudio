using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>The prefab library, exposed to JS as <c>wms.prefabs</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class PrefabsScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "prefabs";

    public PrefabsScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private PrefabSystem Prefabs => _context.Prefabs;

    [ScriptFunction]
    public PrefabDescriptor[] List() => Prefabs.All.Select(Describe).ToArray();

    /// <summary>Saves the entity and its descendants as a new prefab, undoably.</summary>
    [ScriptFunction]
    public PrefabDescriptor Save(ScriptEntityHandle handle, string name)
    {
        if (handle.Resolve() is not SceneEntity source)
        {
            throw new InvalidOperationException("That handle does not refer to a scene entity.");
        }

        return Describe(Prefabs.Save(source, name));
    }

    /// <summary>
    /// Spawns a prefab, by name or id, with its root at world (x, y, z), undoably. Returns the spawned
    /// entities, root first; with <paramref name="select"/> they also become the selection.
    /// </summary>
    [ScriptFunction]
    public ScriptEntityHandle[] Spawn(object nameOrId, double x, double y, double z, bool select = false)
    {
        IReadOnlyList<SceneEntity> spawned = Prefabs.Spawn(Find(nameOrId), new Vector3((float)x, (float)y, (float)z));
        if (select)
        {
            _context.Selection.Clear();
            foreach (SceneEntity entity in spawned)
            {
                _context.Selection.Add(entity);
            }
        }

        return spawned.Select(entity => new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity)).ToArray();
    }

    /// <summary>Deletes a prefab, by name or id, along with its template, undoably.</summary>
    [ScriptFunction]
    public void Delete(object nameOrId) => Prefabs.Delete(Find(nameOrId));

    private Prefab Find(object nameOrId)
    {
        if (nameOrId is string name)
        {
            Prefab[] matches = Prefabs.All.Where(prefab => prefab.Name == name).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException($"No prefab named '{name}'."),
                _ => throw new InvalidOperationException($"{matches.Length} prefabs are named '{name}'; use the id."),
            };
        }

        int id = Convert.ToInt32(nameOrId, CultureInfo.InvariantCulture);
        return Prefabs.All.FirstOrDefault(prefab => prefab.RecordId == id)
            ?? throw new InvalidOperationException($"No prefab with id {id}.");
    }

    private PrefabDescriptor Describe(Prefab prefab) => new(prefab, Prefabs.EntityCount(prefab));
}

/// <summary>A <see cref="Prefab"/> as <c>wms.prefabs</c> reports it.</summary>
public sealed class PrefabDescriptor
{
    public PrefabDescriptor(Prefab prefab, int entityCount)
    {
        Id = prefab.RecordId;
        Name = prefab.Name;
        EntityCount = entityCount;
    }

    [ScriptProperty] public int? Id { get; }
    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public int EntityCount { get; }
}
