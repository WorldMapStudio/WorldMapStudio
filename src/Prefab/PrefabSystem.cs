using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Owns the prefab library: saved <see cref="Prefab"/> catalog rows plus the
/// <see cref="SceneEntity"/> template subtree each one names, found via
/// <see cref="PrefabRootComponent"/>. Templates are ordinary scene entities tagged with
/// <see cref="LibraryMap"/>, a reserved id no real map ever uses, so the existing
/// <see cref="SceneEntity"/> + <see cref="ISceneComponentPersistence"/> machinery persists them
/// without any new serialization — see <c>.claude/plans</c> for the full reasoning. Loaded once,
/// kept in <see cref="EditorContext.Scene"/> marked peripheral so they are never drawn, listed or
/// picked, and marked resident (see <see cref="SceneEntityRegistry.IsResident"/>) so streaming never
/// sweeps them, but still read by <see cref="DatabaseSystem"/>'s save-vs-delete check on commit.
///
/// Spawning clones a template into fresh, independent entities (like copy/paste) — nothing keeps
/// referencing the <see cref="Prefab"/> afterward, so editing the template later never changes an
/// instance already placed.
/// </summary>
public sealed class PrefabSystem : IWorldParticipant
{
    /// <summary>Never a real, created map: <see cref="MapId"/> carries no DB/FK constraint, and the
    /// map-creation UI (<c>MapSelectDialog</c>) clamps new ids to non-negative, so this can never
    /// collide. Streaming and export scans are always filtered by <c>MapId == CurrentMap</c>, so
    /// entities tagged with this id are provably never touched by either.</summary>
    public static readonly MapId LibraryMap = new(-1);

    // Covers any plausible authored coordinate; only used to pull the whole (small) prefab library in
    // one go via the existing bounds-scoped ScanAsync.
    private static readonly Aabb LibraryBounds = new(
        new Vector3(-1.0e8f, -1.0e8f, -1.0e8f), new Vector3(2.0e8f, 2.0e8f, 2.0e8f));

    // The library load wants every template built, so nothing is reported as already loaded.
    private static readonly HashSet<long> Nothing = [];

    private readonly EditorContext _context;

    public PrefabSystem(EditorContext context)
    {
        _context = context;
    }

    public IEnumerable<Prefab> All => _context.Catalog.OfType<Prefab>();

    /// <summary>Loads every prefab template subtree once. Called from <see cref="EditorContext.LoadContent"/>;
    /// the <see cref="Prefab"/> catalog itself is loaded earlier, by <see cref="DatabaseSystem"/>'s own
    /// <see cref="IWorldParticipant"/>.</summary>
    public void LoadLibrary()
    {
        (List<SceneEntity> entities, List<CatalogEntity> catalog) = BlockingWork.Run(ScanLibraryAsync);

        // Published before the entities themselves so a template placement's model is never observed
        // unresolved — the same order StreamingSystem.Reconcile uses.
        foreach (CatalogEntity entity in catalog)
        {
            if (!_context.Catalog.Contains(entity))
            {
                _context.Catalog.Add(entity);
            }
        }

        foreach (SceneEntity entity in entities)
        {
            entity.Component<ProceduralComponent>()?.ClearAttachment();
            _context.Scene.Add(entity);
            _context.Scene.SetPeripheral(entity, true);
            _context.Scene.SetResident(entity, true);
        }
    }

    // After images (channel/material catalogs a template might reference don't have to exist, but
    // loading after them keeps the same relative order LoadContent used).
    float IWorldParticipant.LoadPriority => 5f;

    string? IWorldParticipant.LoadStep => "Loading prefabs";

    void IWorldParticipant.LoadWorld() => LoadLibrary();

    // Resident entities are exempt from streaming, so nothing else ever removes them from the scene
    // registry — unlike an ordinary streamed entity, which simply stops being re-added once streaming
    // stops scanning, a template left behind here would look like a leak the verification pass has to
    // report every single reload.
    void IWorldParticipant.UnloadWorld()
    {
        foreach (SceneEntity entity in _context.Scene.Entities.Where(entity => entity.Map == LibraryMap).ToList())
        {
            entity.DestroyRepresentation();
            _context.Scene.Remove(entity);
        }
    }

    /// <summary>The template's root entity, or null if the prefab's row exists but its subtree isn't
    /// loaded (e.g. failed to load).</summary>
    public SceneEntity? RootOf(Prefab prefab) => _context.Scene.Entities.FirstOrDefault(entity =>
        entity.Map == LibraryMap && entity.Component<PrefabRootComponent>()?.PrefabId == prefab.RecordId);

    /// <summary>
    /// Builds (but does not apply or record) the command that saves <paramref name="source"/> and its
    /// descendants as a new named prefab. The caller applies and records it, like every other add-flow
    /// in this codebase.
    /// </summary>
    public IEditCommand BuildSaveCommand(SceneEntity source, string name)
    {
        var prefab = new Prefab { Name = name.Trim().Length == 0 ? "Prefab" : name.Trim() };
        _context.Catalog.AssignId(prefab);

        (SceneEntity root, List<SceneEntity> all) = CloneSubtree(source, LibraryMap);
        root.AddComponent(new PrefabRootComponent { PrefabId = prefab.RecordId!.Value });

        var commands = new List<IEditCommand> { new CreateCatalogEntityCommand(_context.Catalog, prefab) };
        commands.AddRange(all.Select(entity => (IEditCommand)new CreateLibraryEntityCommand(_context.Scene, entity)));
        return new BatchEditCommand($"Save Prefab '{prefab.Name}'", commands);
    }

    /// <summary>
    /// Builds (but does not apply or record) the command that spawns an independent copy of
    /// <paramref name="prefab"/>'s template into the current map, with its root placed at
    /// <paramref name="at"/>. Also returns every spawned entity, so the caller can select them.
    /// </summary>
    public (IEditCommand Command, IReadOnlyList<SceneEntity> Entities) BuildSpawnCommand(Prefab prefab, Vector3 at)
    {
        SceneEntity template = RootOf(prefab) ?? throw new InvalidOperationException(
            $"Prefab '{prefab.Name}' has no loaded template.");

        (SceneEntity root, List<SceneEntity> all) = CloneSubtree(template, _context.Maps.CurrentMap);
        if (root.Component<PrefabRootComponent>() is { } marker)
        {
            // A spawned instance is an ordinary entity, not another prefab root.
            root.RemoveComponent(marker);
        }

        Vector3 delta = at - root.Transform.Origin;
        foreach (SceneEntity entity in all)
        {
            Transform3D transform = entity.Transform;
            entity.Transform = new Transform3D(transform.Basis, transform.Origin + delta);
        }

        var commands = all.Select(entity => (IEditCommand)new CreateEntityCommand(_context.Scene, entity)).ToList();
        return (new BatchEditCommand($"Spawn Prefab '{prefab.Name}'", commands), all);
    }

    /// <summary>Builds (but does not apply or record) the command that deletes a prefab: its catalog
    /// row and its whole template subtree.</summary>
    public IEditCommand BuildDeleteCommand(Prefab prefab)
    {
        var commands = new List<IEditCommand> { new DeleteCatalogEntityCommand(_context.Catalog, prefab) };
        if (RootOf(prefab) is { } root)
        {
            commands.AddRange(Family(root).Select(entity => (IEditCommand)new DeleteLibraryEntityCommand(_context.Scene, entity)));
        }

        return new BatchEditCommand($"Delete Prefab '{prefab.Name}'", commands);
    }

    private async Task<(List<SceneEntity> Entities, List<CatalogEntity> Catalog)> ScanLibraryAsync()
    {
        var result = new List<SceneEntity>();
        var catalog = new List<CatalogEntity>();
        foreach (Storage storage in _context.Database.Storages)
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                // Publishing: templates are loaded straight into the live scene registry below.
                SceneEntityScan scan = await factory.ScanAsync(LibraryMap, LibraryBounds, Nothing, publishing: true).ConfigureAwait(false);
                result.AddRange(scan.Built);
                catalog.AddRange(scan.Catalog);
            }
        }

        return (result, catalog);
    }

    /// <summary>Clones <paramref name="source"/> and its descendants, relinking parents among the
    /// clones and retargeting every clone to <paramref name="map"/>. Mirrors <see cref="SceneClipboard"/>'s
    /// clone logic, scoped to one root's own subtree instead of an arbitrary selection.</summary>
    private static (SceneEntity Root, List<SceneEntity> All) CloneSubtree(SceneEntity source, MapId map)
    {
        List<SceneEntity> family = Family(source).ToList();
        var clones = new Dictionary<SceneEntity, SceneEntity>();
        foreach (SceneEntity entity in family)
        {
            SceneEntity clone = entity.Clone();
            clone.Map = map;
            clones[entity] = clone;
        }

        foreach (SceneEntity entity in family)
        {
            if (entity.Parent is { } parent && clones.TryGetValue(parent, out SceneEntity? parentClone))
            {
                clones[entity].Parent = parentClone;
            }
        }

        return (clones[source], clones.Values.ToList());
    }

    private static IEnumerable<SceneEntity> Family(SceneEntity root)
    {
        yield return root;
        foreach (SceneEntity child in root.Children)
        {
            foreach (SceneEntity descendant in Family(child))
            {
                yield return descendant;
            }
        }
    }
}
