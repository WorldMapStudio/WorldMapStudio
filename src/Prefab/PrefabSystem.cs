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
/// picked, but still read by <see cref="DatabaseSystem"/>'s save-vs-delete check on commit.
///
/// Spawning clones a template into fresh, independent entities (like copy/paste) — nothing keeps
/// referencing the <see cref="Prefab"/> afterward, so editing the template later never changes an
/// instance already placed.
/// </summary>
public sealed class PrefabSystem
{
    /// <summary>Never a real, created map: <see cref="MapId"/> carries no DB/FK constraint, and the
    /// map-creation UI (<c>MapSelectOperation</c>) clamps new ids to non-negative, so this can never
    /// collide. Streaming and export scans are always filtered by <c>MapId == CurrentMap</c>, so
    /// entities tagged with this id are provably never touched by either.</summary>
    public static readonly MapId LibraryMap = new(-1);

    // Covers any plausible authored coordinate; only used to pull the whole (small) prefab library in
    // one go via the existing bounds-scoped ScanAsync.
    private static readonly Aabb LibraryBounds = new(
        new Vector3(-1.0e8f, -1.0e8f, -1.0e8f), new Vector3(2.0e8f, 2.0e8f, 2.0e8f));

    private readonly EditorContext _context;

    public PrefabSystem(EditorContext context)
    {
        _context = context;
    }

    public IEnumerable<Prefab> All => _context.Catalog.OfType<Prefab>();

    /// <summary>Loads every saved <see cref="Prefab"/> row. Called once from <see cref="EditorContext.LoadContent"/>,
    /// like <see cref="ProceduralSystem.LoadCatalog"/>.</summary>
    public void LoadCatalog() => _context.Database.LoadCatalog<Prefab>();

    /// <summary>Loads every prefab template subtree once. Called from <see cref="EditorContext.LoadContent"/>
    /// right after <see cref="LoadCatalog"/>.</summary>
    public void LoadLibrary()
    {
        List<SceneEntity> entities = BlockingWork.Run(ScanLibraryAsync);
        foreach (SceneEntity entity in entities)
        {
            _context.Scene.Add(entity);
            _context.Scene.SetPeripheral(entity, true);
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
    /// <paramref name="at"/>.
    /// </summary>
    public IEditCommand BuildSpawnCommand(Prefab prefab, Vector3 at)
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
        return new BatchEditCommand($"Spawn Prefab '{prefab.Name}'", commands);
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

    private async Task<List<SceneEntity>> ScanLibraryAsync()
    {
        var result = new List<SceneEntity>();
        foreach (Storage storage in _context.Database.Storages)
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                IReadOnlyList<SceneEntity> loaded = await factory.ScanAsync(LibraryMap, LibraryBounds).ConfigureAwait(false);
                result.AddRange(loaded);
            }
        }

        return result;
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
