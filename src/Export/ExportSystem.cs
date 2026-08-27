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

    public WorkHandle Run(IChunkExportScript exporter, ChunkExportScope scope)
    {
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
        LandscapeSettings? settings = LoadLandscapeSettings(map);
        if (settings == null)
        {
            return null;
        }

        var builder = new LandscapeBuilder(settings, catalog, functions);
        Aabb scan = ScanBounds(builder, coord);
        IReadOnlyList<SceneEntity> entities = await ScanSceneAsync(map, scan).ConfigureAwait(false);
        List<ILandscapeDeformer> deformers = entities
            .SelectMany(entity => entity.Components)
            .OfType<ILandscapeDeformer>()
            .ToList();
        return builder.BuildOne(coord, deformers);
    }

    private async Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region)
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
