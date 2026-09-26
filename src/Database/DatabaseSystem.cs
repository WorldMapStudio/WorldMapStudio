using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's data backends. Storages self-register with [Subsystem(nameof(DatabaseSystem))]
/// and are constructed by the generated InitializeSubsystems(), so the built-in "Editor" storage and
/// any plugin storages register without touching this class. A plain member of
/// <see cref="EditorContext"/> (the core spine, not an extension point), but itself a host.
/// On <see cref="Startup"/> it ensures each storage's DoltLite file exists and its schema is created.
/// </summary>
[SubsystemHost(typeof(Storage))]
public sealed partial class DatabaseSystem : ISubsystemHost, IEditSessionStore, IWorldParticipant
{
    private readonly EditorContext _context;

    public IEnumerable<Storage> Storages => Subsystems;

    /// <summary>Which storage and factory own a scene entity, by type — see <see cref="SceneEntitySources"/>.</summary>
    public SceneEntitySources SceneSources { get; }

    public DatabaseSystem(EditorContext context)
    {
        DoltliteRuntime.EnsureInitialized();
        _context = context;
        SceneSources = new SceneEntitySources(this);
        InitializeSubsystems();
        BindLocations();
    }

    public EditorContext Context => _context;

    // Each storage reads its location from the project's settings, which are seeded with the
    // storage's defaults the first time a project uses it.
    private void BindLocations()
    {
        foreach (Storage storage in Storages)
        {
            if (!storage.OwnsLocation)
            {
                continue;
            }

            StorageLocation location = _context.Project.GetOrAddStorageLocation(storage.Name, storage.CreateDefaultLocation());
            storage.BindLocation(location);
        }
    }

    /// <summary>Ensures each storage's DoltLite file exists (creating its directory on first use) and
    /// its schema is created.</summary>
    public void Startup()
    {
        foreach (Storage storage in Storages)
        {
            // A storage that shares another's location (see Storage.OwnsLocation) doesn't create its
            // own file — the owning storage already did — but it still gets its own EnsureSchema()
            // call, since it owns a disjoint set of tables within that shared file.
            if (storage.OwnsLocation)
            {
                EnsureDatabaseFile(storage);
            }

            try
            {
                storage.EnsureSchema();
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Storage '{storage.Name}' schema check failed: {e.Message}");
                continue;
            }

            // Seeds run here, before the caller checks for schema drift (see EditorContext.Startup),
            // so a plugin's reference data lands before the migration gate ever sees the database.
            try
            {
                BlockingWork.Run(storage.ApplySeedsAsync);
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Storage '{storage.Name}' seed apply failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Every stored scene entity overlapping <paramref name="region"/>, asked of each storage's
    /// <see cref="ISceneEntityFactory"/>s under that storage's read lock. Reads storage rather than the
    /// loaded scene, so it answers for maps that are not open and regions nothing has streamed in —
    /// what offline work (a batch job, a landscape build for another map) needs.
    /// </summary>
    public async Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region)
    {
        // Not publishing: this result belongs to the caller, not the live editor — see
        // SceneEntityScanCatalog.Publishing. Each entity's own resolved models ride on its
        // components' attachments instead.
        FactoryScanResult scan = await ScanFactoriesAsync(map, region, null, publishing: false, Context.Bridge.Snapshot)
            .ConfigureAwait(false);
        return scan.Built;
    }

    /// <summary>
    /// Loads a catalog whole into <see cref="EditorContext.Catalog"/>, replacing anything of that type
    /// already loaded — except an entity the active edit session still has pinned, which survives
    /// untouched (see <see cref="IsPinned"/>), so a type-scoped reload can never silently discard an
    /// uncommitted create or edit. Catalog lifetime belongs to whichever system owns the catalog —
    /// nothing streams these — so it calls this when it needs the set and
    /// <see cref="UnloadCatalog{TEntity}"/> when it is done.
    /// </summary>
    public IReadOnlyList<TEntity> LoadCatalog<TEntity>() where TEntity : CatalogEntity =>
        LoadCatalog(typeof(TEntity)).Cast<TEntity>().ToList();

    /// <summary>Type-based counterpart of <see cref="LoadCatalog{TEntity}"/> — what <see cref="IWorldParticipant.LoadWorld"/>
    /// uses to bulk-load every registered catalog type without naming each one.</summary>
    private IReadOnlyList<CatalogEntity> LoadCatalog(Type entityType)
    {
        _context.Catalog.RemoveAll(entityType, IsPinned);

        var loaded = new List<CatalogEntity>();
        foreach (Storage storage in Storages)
        {
            foreach (ICatalogEntityFactory factory in storage.CatalogFactories)
            {
                if (!entityType.IsAssignableFrom(factory.EntityType))
                {
                    continue;
                }

                try
                {
                    foreach (CatalogEntity entity in Read(storage, factory.LoadAllAsync))
                    {
                        if (entityType.IsInstanceOfType(entity))
                        {
                            _context.Catalog.Add(entity);
                            loaded.Add(entity);
                        }
                    }
                }
                catch (Exception e)
                {
                    GD.PushError($"[Database] Loading catalog {entityType.Name} from '{storage.Name}' failed: {e.Message}");
                }
            }
        }

        return loaded;
    }

    /// <summary>Drops a loaded catalog. Entities the edit session pinned stay alive until it ends.</summary>
    public void UnloadCatalog<TEntity>() where TEntity : CatalogEntity => _context.Catalog.RemoveAll<TEntity>(IsPinned);

    /// <summary>
    /// Every distinct <see cref="ICatalogEntityFactory.EntityType"/> registered across every storage —
    /// what <see cref="IWorldParticipant"/> loads/unloads as one project-wide bulk operation instead of
    /// each catalog's owning system (or, for a plugin catalog with no owning system, a bespoke
    /// <see cref="IWorldParticipant"/> written solely to shuttle it in and out) doing so itself.
    /// </summary>
    private IEnumerable<Type> CatalogEntityTypes() =>
        Storages.SelectMany(storage => storage.CatalogFactories).Select(factory => factory.EntityType).Distinct();

    /// <summary>Where the row ids of <paramref name="entityType"/> are stored, from its eager or lazy
    /// factory; null when the type has none or its lazy factory is not an <see cref="IRecordIdSource"/>.</summary>
    public IRecordIdSource? FindRecordIdSource(Type entityType) =>
        (IRecordIdSource?)Storages.SelectMany(storage => storage.CatalogFactories)
            .FirstOrDefault(factory => factory.EntityType == entityType)
        ?? Storages.SelectMany(storage => storage.LazyCatalogFactories)
            .FirstOrDefault(factory => factory.EntityType == entityType) as IRecordIdSource;

    /// <summary>Every distinct <see cref="ILazyCatalogEntityFactory.EntityType"/> registered across
    /// every storage. A lazy factory never bulk-loads, so this plays no part in
    /// <see cref="IWorldParticipant.LoadWorld(PhaseTimings)"/> — but whatever it opened on demand still
    /// has to leave <see cref="CatalogEntityRegistry"/> on world unload, same as an eager catalog.</summary>
    private IEnumerable<Type> LazyCatalogEntityTypes() =>
        Storages.SelectMany(storage => storage.LazyCatalogFactories).Select(factory => factory.EntityType).Distinct();

    // Loads before anything that resolves against a catalog (landscape channels, mesh material
    // presets, ...), and — since WorldLifecycle unloads in exact reverse — unloads only after every
    // other participant's UnloadWorld has already dropped whatever referenced them.
    float IWorldParticipant.LoadPriority => -1f;

    string? IWorldParticipant.LoadStep => "Loading catalogs";

    // Broken down per catalog type, nested inside the "Loading catalogs" phase WorldLifecycle already
    // measures around this call — this is the whole reason DatabaseSystem opts into the timings
    // overload instead of the plain one: "Loading catalogs" alone never says which of the (often
    // dozens of) registered catalog types is the one worth optimizing.
    void IWorldParticipant.LoadWorld(PhaseTimings timings)
    {
        foreach (Type entityType in CatalogEntityTypes())
        {
            using (timings.Measure($"catalog: {entityType.Name}"))
            {
                LoadCatalog(entityType);
            }
        }
    }

    void IWorldParticipant.UnloadWorld()
    {
        foreach (Type entityType in CatalogEntityTypes())
        {
            _context.Catalog.RemoveAll(entityType, IsPinned);
        }

        foreach (Type entityType in LazyCatalogEntityTypes())
        {
            _context.Catalog.RemoveAll(entityType, IsPinned);
        }
    }

    // Mirrors StreamingSystem.IsPinned — the same "is the active session still holding this for an
    // uncommitted edit" check, applied to a catalog entity instead of a scene one. Untyped (rather than
    // generic over TEntity) on purpose: Func<in T> is contravariant, so this satisfies
    // RemoveAll<TEntity>'s Func<TEntity, bool> for whatever TEntity the caller asks for.
    //
    // Internal rather than private so EditorStorage.FindMapOnlyResourcesAsync can reuse it for its own
    // "still live" guard instead of reimplementing the pinned check.
    internal bool IsPinned(CatalogEntity entity)
    {
        foreach (IEntity pinned in _context.EditSessions.Active.Pinned)
        {
            if (ReferenceEquals(pinned, entity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Persists everything the session touched, grouped per storage into one transaction each — scene
    /// and catalog entities together, so a session that edited both commits atomically. A pinned entity
    /// still registered (in the scene or the catalog) is saved; one that has left (an undone creation or
    /// a deletion) is deleted.
    ///
    /// Two ordered passes. The primary pass commits every entity to the storage that owns it, the
    /// editor's own storage last, so a key a database assigns on insert (a creature's guid) is written
    /// back before the second pass reads it. The attachment pass then commits the tags and attached
    /// components of entities living outside the editor's storage — see
    /// <see cref="EditorStorage.CommitBridgedAsync"/>. The two are separate transactions, since nothing
    /// spans databases: if the second fails, the entities themselves are saved and the error is reported.
    ///
    /// Reached through <see cref="IEditSessionStore"/> from <see cref="EditSessionManager.Commit"/>,
    /// never called directly — committing is one act, not a write followed by a clear the caller has to
    /// remember.
    /// </summary>
    public void Persist(EditSession session)
    {
        var committed = new HashSet<IEntity>();
        var failed = new HashSet<Storage>();

        // Read before the primary pass deletes anything: some factories forget a row's key once it is gone.
        Dictionary<SceneEntity, long> doomedKeys = KeysOfDeletedBridged(session);

        foreach (Storage storage in CommitOrder())
        {
            var saves = new List<IEntity>();
            var deletes = new List<IEntity>();

            foreach (IEntity entity in session.Pinned)
            {
                // Derived entities are computed, never stored. The session already refuses to pin
                // one, so this is the second lock on the door that actually matters: whatever else
                // goes wrong, a computed result must not reach the database.
                if (entity is IDerivedEntity)
                {
                    continue;
                }

                bool owned = entity is SceneEntity scene
                    ? ReferenceEquals(SceneSources.StorageOf(scene), storage)
                    : storage.EntityFactories.Any(factory => factory.Handles(entity));
                if (owned)
                {
                    (IsLoaded(entity) ? saves : deletes).Add(entity);
                }
            }

            if (saves.Count == 0 && deletes.Count == 0)
            {
                continue;
            }

            try
            {
                BlockingWork.Run(() => storage.CommitAsync(saves, deletes));
                foreach (IEntity entity in saves)
                {
                    committed.Add(entity);
                }

                foreach (IEntity entity in deletes)
                {
                    committed.Add(entity);
                }
            }
            catch (Exception e)
            {
                failed.Add(storage);
                GD.PushError($"[Database] Commit failed for '{storage.Name}': {e.Message}");
            }
        }

        CommitBridged(session, failed, doomedKeys);

        if (committed.Count > 0)
        {
            try
            {
                _context.ChunkChanges.RecordCommit(session, committed.Contains);
            }
            catch (Exception e)
            {
                GD.PushError($"[Database] Recording chunk changes failed: {e.Message}");
            }
        }
    }

    // The editor's own storage last: see Persist.
    private IEnumerable<Storage> CommitOrder() =>
        Storages.Where(candidate => candidate.GetType() != typeof(EditorStorage)).Concat(Storages.OfType<EditorStorage>());

    public void Shutdown()
    {
        foreach (FileStream lockFile in _lockFiles.Values)
        {
            lockFile.Dispose();
        }

        _lockFiles.Clear();

        // Releases the file handles DoltLite/SQLite pooling kept open, so the file is free for another
        // process (or export) to use immediately rather than whenever finalizers happen to run.
        SqliteConnection.ClearAllPools();
    }

    // Whether the entity is still loaded, which is what separates a save from a delete.
    private bool IsLoaded(IEntity entity) => entity switch
    {
        SceneEntity scene => _context.Scene.Contains(scene),
        CatalogEntity catalog => _context.Catalog.Contains(catalog),
        _ => throw new ArgumentOutOfRangeException(nameof(entity), entity.GetType(), "Unknown entity kind."),
    };

    // Reads under the storage's reader lock. Blocks the caller — see <see cref="BlockingWork"/>.
    private static IReadOnlyList<T> Read<T>(Storage storage, Func<Task<IReadOnlyList<T>>> read) =>
        BlockingWork.Run(async () =>
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            return await read().ConfigureAwait(false);
        });

    private readonly Dictionary<Storage, FileStream> _lockFiles = new();

    // The file itself is created lazily by SQLite/DoltLite on first open; this only has to make sure
    // the directory exists and warn if another instance already holds the file.
    private void EnsureDatabaseFile(Storage storage)
    {
        StorageLocation location = storage.Location;
        if (location.DatabasePath.Length == 0)
        {
            location.DatabasePath = DefaultDatabasePath(storage.Name);
        }

        try
        {
            string? directory = Path.GetDirectoryName(location.DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string lockPath = location.DatabasePath + ".wms-lock";
            _lockFiles[storage] = new FileStream(lockPath, FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, FileShare.None);
            GD.Print($"[Database] Storage '{storage.Name}' ready ({location.DatabasePath}).");
        }
        catch (IOException e)
        {
            GD.PushError($"[Database] Storage '{storage.Name}': '{location.DatabasePath}' looks like it's open in another instance ({e.Message}).");
        }
    }

    private string DefaultDatabasePath(string storageName) =>
        Path.Combine(ProjectStore.ProjectFolder(_context.Project.Name), "data", $"{storageName.ToLowerInvariant()}.doltlite");
}
