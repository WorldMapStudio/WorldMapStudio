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

    /// <summary>Schedules an export, or refuses (returning null) while an exclusive world operation
    /// (see <see cref="WorldOperations"/>) is rewriting the database an export would read from. Not
    /// gated on the edit session being dirty — an export only ever reads *committed* chunk changes,
    /// so mid-edit is not a reason to refuse one.</summary>
    public WorkHandle? Run(IChunkExportScript exporter, ChunkExportScope scope)
    {
        if (Context.Operations.ActiveOperation is { } operation)
        {
            GD.PushWarning($"[Export] Refused to start '{exporter.DisplayName}': '{operation}' is running.");
            return null;
        }

        IReadOnlyList<ChunkChange> chunks = Changes.DirtyFor(exporter.Id, scope);
        LandscapeCatalog catalog = Context.Landscape.Catalog;
        LandscapeFunctions functions = Context.Landscape.Functions;

        return WorkQueue.Schedule($"{exporter.DisplayName}: {chunks.Count} chunks", async work =>
        {
            work.Step("Exporting");
            var context = new ChunkExportContext(this, catalog, functions);
            ChunkExportResult result = await exporter.ExportAsync(context, chunks, work).ConfigureAwait(false);

            if (result.ExportedChunks == chunks.Count && chunks.Count > 0)
            {
                work.Step("Updating export state");
                Changes.MarkExported(exporter.Id, chunks);
            }
        });
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
