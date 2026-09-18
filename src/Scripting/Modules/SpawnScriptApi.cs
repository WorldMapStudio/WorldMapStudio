using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Scripted (and UI-parallel) creation of any registered <see cref="ISpawnFactory"/> entity, exposed
/// to JS as <c>wms.spawn</c> — the scene-entity sibling of <see cref="CatalogScriptApi"/>. Knows
/// nothing about which spawn kinds exist: a scene-entity factory becomes scriptably creatable purely
/// by implementing <see cref="ISpawnFactory"/>, exactly as a catalog becomes scriptable purely by
/// implementing <see cref="ICatalogBrowser"/>.
///
/// Only creation lives here. Editing an existing spawn is already generic without this: a handle's
/// <c>[ScriptProperty(Mutable = true)]</c> fields are writable directly, component fields go through
/// <c>wms.scene.Get-</c>/<c>SetComponentField</c>, and movement through <c>wms.scene.SetPosition</c> —
/// all of it works on any <see cref="SceneEntity"/> subclass a factory here produces with no further
/// wiring, the same way editing an opened catalog entity needs nothing beyond
/// <see cref="ICatalogBrowser"/>'s own <c>Create</c>/<c>Open</c>.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class SpawnScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "spawn";

    public float Priority => 0.0f;

    public SpawnScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Every scriptable spawn kind's name, as accepted by <see cref="Create"/>.</summary>
    [ScriptFunction]
    public string[] List() => Factories.Select(factory => factory.SpawnKind).OrderBy(name => name).ToArray();

    /// <summary>Creates a new entity of the named kind on <paramref name="map"/>, at
    /// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) with a yaw of
    /// <paramref name="yawRadians"/> about +Y — undoably, exactly like placing one by hand. The key is
    /// whatever that spawn kind identifies its "what to create" by, as a string (e.g. a creature
    /// template entry).</summary>
    [ScriptFunction]
    public ScriptEntityHandle Create(string kind, string key, int map, double x, double y, double z, double yawRadians = 0.0)
    {
        var transform = new Transform3D(new Basis(Vector3.Up, (float)yawRadians), new Vector3((float)x, (float)y, (float)z));
        SceneEntity entity = Factory(kind).Create(_context, new MapId(map), transform, key);
        return ToHandle(entity);
    }

    private IEnumerable<ISpawnFactory> Factories =>
        _context.Database.Storages.SelectMany(storage => storage.Spawners);

    private ISpawnFactory Factory(string kind) =>
        Factories.FirstOrDefault(factory => factory.SpawnKind == kind)
            ?? throw new InvalidOperationException($"No spawn kind named '{kind}'. Known: {string.Join(", ", List())}.");

    private ScriptEntityHandle ToHandle(SceneEntity entity) =>
        new(_context.Scene, _context.Catalog, _context.EditSessions, entity);
}
