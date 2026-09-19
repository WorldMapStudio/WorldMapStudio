using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Hydrates storage-scanned deformers so an offline build sees what the live editor would have put on
/// them. Runs in three stages, deliberately mirroring <see cref="LandscapeBuilder"/>'s own staging:
/// declare, satisfy, apply.
///
/// The declare stage runs before anything is filtered by <see cref="ILandscapeDeformer.InfluenceBounds"/>,
/// which matters: a <see cref="ProceduralComponent"/>'s influence bounds are zero until it has been
/// prepared, so filtering first would drop exactly the deformers that need preparing.
/// </summary>
internal static class LandscapeBuildPreparation
{
    // How many main-thread Prepare calls to run between frame-budget yields, so a tile with hundreds
    // of procedural placements does not blow through PumpMainThread's per-frame budget in one go.
    private const int MainThreadSlice = 64;

    public static async Task RunAsync(
        EditorContext context,
        Aabb region,
        IReadOnlyList<ILandscapeDeformer> deformers,
        WorkContext work)
    {
        List<IPreparableLandscapeDeformer> preparable = deformers.OfType<IPreparableLandscapeDeformer>().ToList();
        if (preparable.Count == 0)
        {
            return;
        }

        var request = new LandscapeBuildRequest(region);
        foreach (IPreparableLandscapeDeformer deformer in preparable)
        {
            request.BeginDeformer(deformer);
            deformer.Request(request);
        }

        if (!request.AnythingRequested)
        {
            return;
        }

        var resources = new LandscapeBuildResources(await LoadImageChunksAsync(context, request).ConfigureAwait(false));

        foreach (IPreparableLandscapeDeformer deformer in preparable)
        {
            if (!request.WantsMainThread(deformer))
            {
                deformer.Prepare(resources);
            }
        }

        List<IPreparableLandscapeDeformer> mainThread = preparable.Where(request.WantsMainThread).ToList();
        if (mainThread.Count == 0)
        {
            return;
        }

        await work.SwitchToMain();
        for (int i = 0; i < mainThread.Count; i++)
        {
            mainThread[i].Prepare(resources);
            if ((i + 1) % MainThreadSlice == 0)
            {
                await work.Yield();
            }
        }

        await work.SwitchToBackground();
    }

    private static async Task<IReadOnlyDictionary<PaintImage, ImageChunkTable>> LoadImageChunksAsync(
        EditorContext context, LandscapeBuildRequest request)
    {
        var tables = new Dictionary<PaintImage, ImageChunkTable>();
        if (request.WantedImageChunks.Count == 0)
        {
            return tables;
        }

        EditorStorage storage = context.Database.EditorStorage;

        Dictionary<PaintImage, IReadOnlyCollection<ImageChunkCoord>> wanted = request.WantedImageChunks
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<ImageChunkCoord>)pair.Value);

        IReadOnlyList<(PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)> rows =
            await storage.LoadImageChunksAsync(wanted).ConfigureAwait(false);

        foreach (IGrouping<PaintImage, (PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)> group in
            rows.GroupBy(row => row.Image))
        {
            var chunks = new Dictionary<ImageChunkCoord, ImageChunk>();
            foreach ((PaintImage _, ImageChunkCoord coord, byte[] pixels) in group)
            {
                chunks[coord] = new ImageChunk(pixels);
            }

            tables[group.Key] = new ImageChunkTable(chunks);
        }

        return tables;
    }
}
