using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public sealed partial class ExportSystem : ISubsystemHost
{
    public EditorContext Context { get; }

    public ChunkChangeRegistry Changes { get; }

    public IEnumerable<IChunkExportScript> Exporters => Subsystems.OfType<IChunkExportScript>();

    public ExportSystem(EditorContext context)
    {
        Context = context;
        Changes = new ChunkChangeRegistry(context);
        InitializeSubsystems();
    }

    public LandscapeSettings? LoadLandscapeSettings(MapId map)
    {
        if (Context.Maps.CurrentMap == map && Context.Landscape.Settings is { } loaded)
        {
            return loaded.Clone();
        }

        foreach (ILandscapeSettingsSource source in Context.Database.Storages.SelectMany(storage => storage.LandscapeSettingsSources))
        {
            try
            {
                if (BlockingWork.Run(() => source.LoadAsync(map)) is { } settings)
                {
                    return settings.Clone();
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Export] Loading landscape settings for map {map.Value} failed: {e.Message}");
            }
        }

        return null;
    }

    /// <summary>Schedules an export behind <see cref="WorldOperations"/>' exclusive gate — refuses
    /// (returning null) if the edit session is dirty or another exclusive operation is already running,
    /// and no session edit can be recorded until the export finishes. An export only ever reads
    /// *committed* chunk changes, so this isn't about correctness so much as making sure nobody starts
    /// painting terrain a running export is about to read, or loses track of an export that's still
    /// in flight; it doesn't touch the loaded world's own content, so it skips the reload
    /// <see cref="WorldOperations.TryRun"/> would otherwise request afterward.</summary>
    public WorkHandle? Run(IChunkExportScript exporter, ChunkExportScope scope)
    {
        IReadOnlyList<ChunkChange> chunks = Changes.DirtyFor(exporter.Id, scope);
        LandscapeCatalog catalog = Context.Landscape.Catalog;
        LandscapeFunctions functions = Context.Landscape.Functions;

        WorkHandle? handle = Context.Operations.TryRun(
            $"{exporter.DisplayName}: {chunks.Count} chunks",
            async work =>
            {
                work.Step("Exporting");
                var context = new ChunkExportContext(this, catalog, functions);
                ChunkExportResult result = await exporter.ExportAsync(context, chunks, work).ConfigureAwait(false);

                if (result.ExportedChunks == chunks.Count && chunks.Count > 0)
                {
                    work.Step("Updating export state");
                    Changes.MarkExported(exporter.Id, chunks);
                }
            },
            out string? blocker,
            reloadAfter: false);

        if (handle == null)
        {
            GD.PushWarning($"[Export] Refused to start '{exporter.DisplayName}': {blocker}");
        }

        return handle;
    }

    internal async Task<LandscapeChunkOutput?> BuildLandscapeChunkAsync(
        MapId map,
        ChunkCoord coord,
        LandscapeCatalog catalog,
        LandscapeFunctions functions)
    {
        LandscapeBuildResult? result = await BuildLandscapeChunksAsync(map, [coord], catalog, functions).ConfigureAwait(false);
        return result?.Chunks.GetValueOrDefault(coord);
    }

    /// <summary>
    /// Builds any number of chunks with one scene scan and one <see cref="LandscapeBuilder.Build"/>
    /// call, rather than one of each per chunk — an exporter writing a whole ADT tile needs its 256
    /// chunks built as a block, not scanned 256 times over. Returns every requested chunk's problems
    /// alongside its output, so a caller does not need a second pass to surface them.
    /// </summary>
    internal async Task<LandscapeBuildResult?> BuildLandscapeChunksAsync(
        MapId map,
        IReadOnlyList<ChunkCoord> coords,
        LandscapeCatalog catalog,
        LandscapeFunctions functions)
    {
        if (coords.Count == 0)
        {
            return null;
        }

        LandscapeSettings? settings = LoadLandscapeSettings(map);
        if (settings == null)
        {
            return null;
        }

        var builder = new LandscapeBuilder(settings, catalog, functions);
        Aabb scan = ScanBounds(builder, coords[0]);
        for (int i = 1; i < coords.Count; i++)
        {
            scan = scan.Merge(ScanBounds(builder, coords[i]));
        }

        IReadOnlyList<SceneEntity> entities = await ScanSceneAsync(map, scan).ConfigureAwait(false);
        List<ILandscapeDeformer> deformers = entities
            .SelectMany(entity => entity.Components)
            .OfType<ILandscapeDeformer>()
            .ToList();
        return builder.Build(coords, deformers);
    }

    /// <summary>An exporter's previously-assigned stable ids, keyed by <see cref="EntityId"/> — for a
    /// target format that needs one but has no id of its own to reuse (e.g. an ADT placement's unique
    /// id). Empty when the editor storage isn't the default <see cref="EditorStorage"/>.</summary>
    public async Task<IReadOnlyDictionary<long, long>> LoadExportedEntityIdsAsync(string exporterId)
    {
        if (Context.Database.Storages.OfType<EditorStorage>().FirstOrDefault() is not { } storage)
        {
            return new Dictionary<long, long>();
        }

        return await storage.LoadExportedEntityIdsAsync(exporterId).ConfigureAwait(false);
    }

    /// <summary>Persists newly-assigned or changed entity ids from <see cref="LoadExportedEntityIdsAsync"/>.</summary>
    public async Task UpsertExportedEntityIdsAsync(string exporterId, IReadOnlyDictionary<long, long> ids)
    {
        if (Context.Database.Storages.OfType<EditorStorage>().FirstOrDefault() is { } storage)
        {
            await storage.UpsertExportedEntityIdsAsync(exporterId, ids).ConfigureAwait(false);
        }
    }

    internal async Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region)
    {
        var result = new List<SceneEntity>();
        foreach (Storage storage in Context.Database.Storages)
        {
            using IDisposable reader = await storage.Lock.ReaderAsync().ConfigureAwait(false);
            foreach (ISceneEntityFactory factory in storage.SceneFactories)
            {
                IReadOnlyList<SceneEntity> loaded = await factory.ScanAsync(map, region).ConfigureAwait(false);
                result.AddRange(loaded);
            }
        }

        return result;
    }

    private static Aabb ScanBounds(LandscapeBuilder builder, ChunkCoord coord)
    {
        Aabb bounds = builder.Grid.BoundsOf(coord);
        foreach (ChunkCoord neighbour in builder.Grid.OverlappingWithHalo(bounds, builder.SampleRadius))
        {
            bounds = bounds.Merge(builder.Grid.BoundsOf(neighbour));
        }

        return bounds;
    }
}
