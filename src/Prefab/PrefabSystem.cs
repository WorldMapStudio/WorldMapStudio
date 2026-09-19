using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Owns the prefab library: saved <see cref="Prefab"/> catalog rows plus the
/// <see cref="SceneEntity"/> templates each one names, found via
/// <see cref="PrefabTemplateComponent"/>. Templates are ordinary scene entities tagged with
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

    private readonly EditorContext _context;

    public PrefabSystem(EditorContext context)
    {
        _context = context;
    }

    public IEnumerable<Prefab> All => _context.Catalog.OfType<Prefab>();

    /// <summary>Loads every prefab template entity once. Called from <see cref="EditorContext.LoadContent"/>;
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

    /// <summary>The prefab's template entities; empty if the prefab's row exists but its entities aren't
    /// loaded (e.g. failed to load).</summary>
    public IReadOnlyList<SceneEntity> TemplateOf(Prefab prefab) => _context.Scene.Entities
        .Where(entity => entity.Map == LibraryMap && entity.Component<PrefabTemplateComponent>()?.PrefabId == prefab.RecordId)
        .ToList();

    /// <summary>
    /// Builds (but does not apply or record) the command that saves <paramref name="sources"/> as a new
    /// named prefab. Entities that can't be duplicated are left out. The caller applies and records it,
    /// like every other add-flow in this codebase.
    /// </summary>
    public IEditCommand BuildSaveCommand(IReadOnlyList<SceneEntity> sources, string name) =>
        BuildSaveCommand(sources, name, out _);

    private IEditCommand BuildSaveCommand(IReadOnlyList<SceneEntity> sources, string name, out Prefab prefab)
    {
        List<SceneEntity> templates = sources.Select(source => source.Clone()).OfType<SceneEntity>().ToList();
        if (templates.Count == 0)
        {
            throw new InvalidOperationException("None of the entities can be saved as a prefab.");
        }

        prefab = new Prefab
        {
            Name = name.Trim().Length == 0 ? "Prefab" : name.Trim(),
            Anchor = SceneClipboard.BoundsBottomCenter(templates),
        };
        _context.Catalog.AssignId(prefab);

        var commands = new List<IEditCommand> { new CreateCatalogEntityCommand(_context.Catalog, prefab) };
        foreach (SceneEntity template in templates)
        {
            template.Map = LibraryMap;
            template.AddComponent(new PrefabTemplateComponent { PrefabId = prefab.RecordId!.Value });
            commands.Add(new CreateLibraryEntityCommand(_context.Scene, template));
        }

        return new BatchEditCommand($"Save Prefab '{prefab.Name}'", commands);
    }

    /// <summary>
    /// Builds (but does not apply or record) the command that spawns an independent copy of
    /// <paramref name="prefab"/>'s template into the current map, with its anchor placed at
    /// <paramref name="at"/>. Also returns every spawned entity, so the caller can select them.
    /// </summary>
    public (IEditCommand Command, IReadOnlyList<SceneEntity> Entities) BuildSpawnCommand(Prefab prefab, Vector3 at)
    {
        IReadOnlyList<SceneEntity> template = TemplateOf(prefab);
        if (template.Count == 0)
        {
            throw new InvalidOperationException($"Prefab '{prefab.Name}' has no loaded template.");
        }

        Vector3 offset = at - prefab.Anchor;
        var spawned = new List<SceneEntity>(template.Count);
        foreach (SceneEntity source in template)
        {
            SceneEntity clone = source.Clone() ?? throw new InvalidOperationException($"'{source.DisplayName}' can't be duplicated.");
            clone.Map = _context.Maps.CurrentMap;

            // A spawned instance is an ordinary entity, not another template member.
            if (clone.Component<PrefabTemplateComponent>() is { } marker)
            {
                clone.RemoveComponent(marker);
            }

            Transform3D placed = clone.Transform;
            clone.Transform = new Transform3D(placed.Basis, placed.Origin + offset);
            spawned.Add(clone);
        }

        var commands = spawned.Select(entity => (IEditCommand)new CreateEntityCommand(_context.Scene, entity)).ToList();
        return (new BatchEditCommand($"Spawn Prefab '{prefab.Name}'", commands), spawned);
    }

    /// <summary>Saves <paramref name="sources"/> as a new named prefab, undoably.</summary>
    public Prefab Save(IReadOnlyList<SceneEntity> sources, string name)
    {
        IEditCommand command = BuildSaveCommand(sources, name, out Prefab prefab);
        command.Apply();
        _context.EditSessions.Record(command);
        return prefab;
    }

    /// <summary>Spawns an independent copy of <paramref name="prefab"/> into the current map with its
    /// anchor at <paramref name="at"/>, undoably, and returns the spawned entities.</summary>
    public IReadOnlyList<SceneEntity> Spawn(Prefab prefab, Vector3 at)
    {
        (IEditCommand command, IReadOnlyList<SceneEntity> entities) = BuildSpawnCommand(prefab, at);
        command.Apply();
        _context.EditSessions.Record(command);
        return entities;
    }

    /// <summary>Deletes a prefab, its catalog row and its template entities, undoably.</summary>
    public void Delete(Prefab prefab)
    {
        IEditCommand command = BuildDeleteCommand(prefab);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    /// <summary>How many entities the prefab's template holds, or 0 when they aren't loaded.</summary>
    public int EntityCount(Prefab prefab) => TemplateOf(prefab).Count;

    /// <summary>Builds (but does not apply or record) the command that deletes a prefab: its catalog
    /// row and its template entities.</summary>
    public IEditCommand BuildDeleteCommand(Prefab prefab)
    {
        var commands = new List<IEditCommand> { new DeleteCatalogEntityCommand(_context.Catalog, prefab) };
        commands.AddRange(TemplateOf(prefab).Select(entity => (IEditCommand)new DeleteLibraryEntityCommand(_context.Scene, entity)));
        return new BatchEditCommand($"Delete Prefab '{prefab.Name}'", commands);
    }

    private async Task<(List<SceneEntity> Entities, List<CatalogEntity> Catalog)> ScanLibraryAsync()
    {
        // Publishing: templates are loaded straight into the live scene registry.
        FactoryScanResult scan = await _context.Database
            .ScanFactoriesAsync(LibraryMap, LibraryBounds, null, publishing: true, _context.Bridge.Snapshot)
            .ConfigureAwait(false);
        return (scan.Built, scan.Catalog);
    }
}
