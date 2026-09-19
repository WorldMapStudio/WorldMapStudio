using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>What <see cref="DatabaseSystem.ScanFactoriesAsync"/> found across every storage.</summary>
/// <param name="Built">Entities the factories constructed, with editor-side data already attached.</param>
/// <param name="Seen">Every (factory entity type, key) the scan matched, built or not.</param>
/// <param name="Catalog">Catalog entities resolved while building.</param>
public readonly record struct FactoryScanResult(
    List<SceneEntity> Built,
    List<(Type Type, long Key)> Seen,
    List<CatalogEntity> Catalog);

public sealed partial class DatabaseSystem
{
    /// <summary>
    /// Runs every storage's scene factories over <paramref name="region"/>, then gives every entity a
    /// factory bridged into the entity table its tags and attached components. Streaming, the prefab
    /// library load and offline scans all read scene entities this way, so they all see the same entity.
    ///
    /// Each storage is read under one reader lock for all its factories rather than one per factory:
    /// re-acquiring per factory lets an unrelated writer wedge in between every one, and each of those
    /// stalls the scan by however long that write runs.
    /// </summary>
    /// <param name="loadedKeys">Per factory entity type, the keys already loaded, which a factory reports
    /// without rebuilding. Null when nothing is loaded.</param>
    /// <param name="bridge">The bridge index snapshot, captured on the main thread by the caller.</param>
    public async Task<FactoryScanResult> ScanFactoriesAsync(
        MapId map,
        Aabb region,
        IReadOnlyDictionary<Type, HashSet<long>>? loadedKeys,
        bool publishing,
        FrozenDictionary<(string Source, long Key), int> bridge)
    {
        var built = new List<SceneEntity>();
        var seen = new List<(Type Type, long Key)>();
        var catalog = new List<CatalogEntity>();
        foreach (Storage storage in Storages)
        {
            long lockClock = DiagnosticLog.Start();
            using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            DiagnosticLog.Log($"  {storage.Name}: reader lock {DiagnosticLog.MillisecondsSince(lockClock):F0}ms");

            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                using IDisposable factoryScope = DiagnosticLog.Scope(factory.GetType().Name);
                long factoryClock = DiagnosticLog.Start();
                HashSet<long> known = loadedKeys != null && loadedKeys.TryGetValue(factory.EntityType, out HashSet<long>? set) ? set : [];
                SceneEntityScan scanned = await factory.ScanAsync(map, region, known, publishing).ConfigureAwait(false);
                DiagnosticLog.Log(
                    $"  {factory.GetType().Name}: {DiagnosticLog.MillisecondsSince(factoryClock):F0}ms, "
                    + $"{scanned.Built.Count} built of {scanned.Keys.Count} in region");
                built.AddRange(scanned.Built);
                catalog.AddRange(scanned.Catalog);
                foreach (long key in scanned.Keys)
                {
                    seen.Add((factory.EntityType, key));
                }
            }
        }

        await AttachBridgedAsync(built, catalog, publishing, bridge).ConfigureAwait(false);
        return new FactoryScanResult(built, seen, catalog);
    }

    // Looks each entity from a bridged factory up in the index. With no hits — the common case — this
    // runs no queries at all.
    private async Task AttachBridgedAsync(
        List<SceneEntity> built,
        List<CatalogEntity> catalog,
        bool publishing,
        FrozenDictionary<(string Source, long Key), int> bridge)
    {
        if (bridge.Count == 0)
        {
            return;
        }

        var byId = new Dictionary<int, SceneEntity>();
        foreach (SceneEntity entity in built)
        {
            if (SceneSources.SourceOf(entity) is ({ } source, { } key) && bridge.TryGetValue((source, key), out int id))
            {
                entity.RecordId = id;
                byId[id] = entity;
            }
        }

        if (byId.Count == 0 || Storages.OfType<EditorStorage>().FirstOrDefault() is not { } editor)
        {
            return;
        }

        using IDisposable read = await editor.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = editor.CreateContext();

        var attached = new SceneEntityScanCatalog(publishing);
        await editor.Attachments.LoadAsync(context, byId, byId.Keys.ToList(), attached).ConfigureAwait(false);
        catalog.AddRange(attached.Loaded);
    }
}
