using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Handed down through one scan so a persister that resolves a lazily-loaded catalog reference (see
/// <see cref="ProceduralComponentPersistence"/>) can report what it loaded, and knows whether the scan
/// it is part of is going to publish into <see cref="CatalogEntityRegistry"/> or not.
/// </summary>
public sealed class SceneEntityScanCatalog(bool publishing)
{
    private readonly List<CatalogEntity> _loaded = [];

    /// <summary>
    /// True for a scan whose entities enter the live scene — <see cref="StreamingSystem"/>,
    /// <see cref="PrefabSystem"/>'s library load — so a persister skips a reference already resolved
    /// in the registry instead of re-reading it, and lets the registry's own publish (not this scan)
    /// own the instance a live placement resolves to.
    ///
    /// False for an offline scan (<see cref="DatabaseSystem.ScanSceneAsync"/>) whose entities never
    /// enter the scene — its results belong to the caller, not the live editor, so it must never
    /// consult the registry: an id it skipped because the registry had it could be evicted by the live
    /// editor between the check and the read, silently leaving the offline consumer with nothing.
    /// </summary>
    public bool Publishing { get; } = publishing;

    /// <summary>Every catalog entity a persister resolved during this scan — what the owning
    /// <see cref="SceneEntityScan.Catalog"/> is built from.</summary>
    public IReadOnlyList<CatalogEntity> Loaded => _loaded;

    public void Add(CatalogEntity entity) => _loaded.Add(entity);
}
