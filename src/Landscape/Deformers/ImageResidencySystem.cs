using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Keeps each <see cref="PaintImage"/>'s resident chunk set matching what the currently streamed-in
/// <see cref="ImageComponent"/> placements actually need, and evicts the rest once total resident
/// memory crosses a budget. Driven once per frame from <see cref="ImageSystem.Update"/>.
///
/// Two halves, both gated on <see cref="StreamingSystem.ScanVersion"/> so this only does work when the
/// load region actually moved:
/// <list type="bullet">
/// <item>Evict — any resident, clean chunk that fell outside every placement's (load-region-grown)
/// footprint is dropped from memory <em>unconditionally</em>, the same way <c>LandscapeChunkLoader</c>
/// drops terrain the moment it leaves the load region — an image's memory footprint must never outlive
/// the view it was loaded for, regardless of how small the image is or how far under budget the editor
/// happens to be. <see cref="BudgetBytes"/> is a second, independent pass on top of that: a backstop
/// for the rarer case where what is currently wanted, on its own, is still too much. A chunk's pixels
/// are untouched in storage either way, so it simply becomes "stored but not resident" — see
/// <see cref="PaintImage.IsStored"/>.</item>
/// <item>Reload — any chunk in the target set that is stored but not resident (typically one evicted
/// earlier, now needed again) gets fetched back off the main thread, one batched query per image.</item>
/// </list>
///
/// This is what makes a canvas far larger than any single view genuinely practical: only the chunks
/// under a streamed-in placement's footprint are ever resident, regardless of how many chunks the image
/// holds in storage. See <c>.godot/ImageChunkPlan.md</c>.
/// </summary>
public sealed class ImageResidencySystem
{
    private readonly EditorContext _context;
    private readonly ImageSystem _images;

    // How recently (in local "reconciliation generations", not wall time) a resident chunk was last
    // wanted by some placement's target set — the LRU signal eviction sorts candidates by. Only
    // entries for currently-resident chunks are kept; see PruneRecency.
    private readonly Dictionary<(PaintImage Image, ImageChunkCoord Coord), int> _lastWanted = [];

    private Task<List<(PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)>>? _pendingLoad;
    private int _lastScanVersion = -1;
    private int _generation;

    public ImageResidencySystem(EditorContext context, ImageSystem images)
    {
        _context = context;
        _images = images;
    }

    /// <summary>Total resident chunk bytes, across every loaded image, eviction tries to stay under.
    /// A judgment-call default rather than a derived value — see the design plan's open questions for
    /// why this eventually wants to be a project setting instead of a constant.</summary>
    public long BudgetBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Whether a chunk load is in flight — <see cref="ImageSystem"/> forwards this as its own
    /// <see cref="IWorldParticipant.IsBusy"/>, gating a reload's unload step the same way
    /// <see cref="StreamingSystem"/> gates on its own pending scan.</summary>
    public bool IsBusy => _pendingLoad != null;

    /// <summary>Observes and applies a landed chunk load without starting a new one — see
    /// <see cref="StreamingSystem.PumpCompletion"/> for why a <see cref="WorldReload"/> needs to call
    /// this itself instead of relying on <see cref="Update"/>, which does not run once it owns the
    /// scene.</summary>
    public void PumpCompletion() => ApplyCompletedLoad();

    /// <summary>Forgets the recency bookkeeping and drops the pending load reference. The resident
    /// pixel data itself lives on the <see cref="PaintImage"/> catalog entities, which
    /// <see cref="ImageSystem"/> unloads separately.</summary>
    public void UnloadWorld()
    {
        _pendingLoad = null;
        _lastWanted.Clear();
        _lastScanVersion = -1;
        _generation = 0;
    }

    public void Update()
    {
        ApplyCompletedLoad();

        // One load in flight at a time, mirroring StreamingSystem's own scan — this is not on any
        // critical path a user is blocked on, so there is no reason to overlap requests.
        if (_pendingLoad != null)
        {
            return;
        }

        StreamingSystem streaming = _context.Streaming;
        if (!streaming.Reconciled || streaming.ScanVersion == _lastScanVersion)
        {
            return;
        }

        _lastScanVersion = streaming.ScanVersion;

        Dictionary<PaintImage, HashSet<ImageChunkCoord>> targets = ComputeTargets(streaming.ScanMap, streaming.LoadRegion);
        Evict(targets);
        EnqueueReload(targets);
    }

    /// <summary>Every image chunk some loaded placement needs, unioned per image — several placements
    /// can share one image, so this is keyed by <see cref="PaintImage"/> identity, not by component.
    /// Peripheral placements (outside the view but inside the load margin) count too: they are exactly
    /// what makes edge terrain sample correctly.</summary>
    private Dictionary<PaintImage, HashSet<ImageChunkCoord>> ComputeTargets(MapId map, Aabb loadRegion)
    {
        var targets = new Dictionary<PaintImage, HashSet<ImageChunkCoord>>();
        foreach (SceneEntity entity in _context.Scene.Entities)
        {
            if (entity.Map != map)
            {
                continue;
            }

            if (entity.Component<ImageComponent>() is not { } component || component.Image is not { } image)
            {
                continue;
            }

            if (component.ChunksNeededFor(loadRegion) is not { } rect)
            {
                continue;
            }

            if (!targets.TryGetValue(image, out HashSet<ImageChunkCoord>? coords))
            {
                coords = [];
                targets[image] = coords;
            }

            coords.UnionWith(rect.Coords());
        }

        return targets;
    }

    /// <summary>Exposed at <c>internal</c> rather than <c>private</c> so a test can drive the eviction
    /// pass directly against a hand-built target set, without needing a live streaming scan to produce
    /// one — see <see cref="ImageTests"/>.</summary>
    internal void Evict(Dictionary<PaintImage, HashSet<ImageChunkCoord>> targets)
    {
        _generation++;
        foreach ((PaintImage image, HashSet<ImageChunkCoord> coords) in targets)
        {
            foreach (ImageChunkCoord coord in coords)
            {
                _lastWanted[(image, coord)] = _generation;
            }
        }

        bool evictedAny = false;

        // Unconditional: nothing keeps a chunk resident just because there happens to be budget to
        // spare. EvictChunk itself refuses a dirty one, so unsaved work is never at risk here.
        foreach (PaintImage image in _images.Images)
        {
            HashSet<ImageChunkCoord> wanted = targets.TryGetValue(image, out HashSet<ImageChunkCoord>? set) ? set : [];
            foreach (ImageChunkCoord coord in image.ChunkCoords.ToList())
            {
                if (!wanted.Contains(coord))
                {
                    evictedAny |= image.EvictChunk(coord);
                }
            }
        }

        // Backstop: everything still resident at this point is in some placement's current target, so
        // this only fires when that target alone is too large — trims the least-recently-wanted of it,
        // accepting that it may reload again the moment it is still wanted next scan.
        long total = 0;
        foreach (PaintImage image in _images.Images)
        {
            total += image.ResidentByteSize;
        }

        if (total > BudgetBytes)
        {
            var candidates = new List<(PaintImage Image, ImageChunkCoord Coord, int LastWanted, long Bytes)>();
            foreach (PaintImage image in _images.Images)
            {
                foreach (ImageChunkCoord coord in image.ChunkCoords.ToList())
                {
                    if (image.IsDirty(coord))
                    {
                        continue;
                    }

                    int lastWanted = _lastWanted.TryGetValue((image, coord), out int gen) ? gen : -1;
                    candidates.Add((image, coord, lastWanted, image.ChunkByteSize));
                }
            }

            foreach ((PaintImage image, ImageChunkCoord coord, int _, long bytes) in candidates.OrderBy(c => c.LastWanted))
            {
                if (total <= BudgetBytes)
                {
                    break;
                }

                evictedAny |= image.EvictChunk(coord);
                total -= bytes;
            }
        }

        if (evictedAny)
        {
            // A landscape chunk built while an evicted coordinate was resident has that contribution
            // baked into its mesh/texture — without this, it keeps showing paint from an image chunk
            // that is no longer even in memory, since nothing else notices residency shrinking (only
            // growing it, via ApplyCompletedLoad, already invalidates).
            _context.Streaming.Invalidate();
        }

        PruneRecency();
    }

    private void PruneRecency()
    {
        List<(PaintImage Image, ImageChunkCoord Coord)>? stale = null;
        foreach ((PaintImage image, ImageChunkCoord coord) in _lastWanted.Keys)
        {
            if (!image.IsResident(coord))
            {
                (stale ??= []).Add((image, coord));
            }
        }

        if (stale == null)
        {
            return;
        }

        foreach ((PaintImage image, ImageChunkCoord coord) in stale)
        {
            _lastWanted.Remove((image, coord));
        }
    }

    private void EnqueueReload(Dictionary<PaintImage, HashSet<ImageChunkCoord>> targets)
    {
        var toLoad = new List<(PaintImage Image, ImageChunkCoord Coord)>();
        foreach ((PaintImage image, HashSet<ImageChunkCoord> coords) in targets)
        {
            foreach (ImageChunkCoord coord in coords)
            {
                if (!image.IsResident(coord) && image.IsStored(coord))
                {
                    toLoad.Add((image, coord));
                }
            }
        }

        if (toLoad.Count == 0)
        {
            return;
        }

        _pendingLoad = LoadAsync(toLoad);
    }

    // One query per image, bounded by that image's own wanted coordinates' bounding box rather than a
    // scan of its whole chunk table — an image can hold far more stored chunks than are ever wanted at
    // once. Explicitly off the main thread: an uncontended reader lock can complete synchronously and
    // leave the whole query on the calling thread otherwise (see LandscapeChunkLoader for the same note).
    private async Task<List<(PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)>> LoadAsync(
        List<(PaintImage Image, ImageChunkCoord Coord)> toLoad)
    {
        await Task.Yield();

        var result = new List<(PaintImage, ImageChunkCoord, byte[])>();
        EditorStorage? storage = _context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();
        if (storage == null)
        {
            return result;
        }

        using IDisposable read = await storage.Lock.ReaderAsync().ConfigureAwait(false);
        await using EditorDbContext context = storage.CreateContext();

        foreach (IGrouping<PaintImage, (PaintImage Image, ImageChunkCoord Coord)> group in toLoad.GroupBy(entry => entry.Image))
        {
            PaintImage image = group.Key;
            int imageId = image.RecordId ?? 0;
            var wanted = new HashSet<ImageChunkCoord>(group.Select(entry => entry.Coord));
            int minX = wanted.Min(c => c.X);
            int maxX = wanted.Max(c => c.X);
            int minY = wanted.Min(c => c.Y);
            int maxY = wanted.Max(c => c.Y);

            List<ImageChunkRecord> rows = await context.ImageChunks.AsNoTracking()
                .Where(row => row.ImageId == imageId
                    && row.ChunkX >= minX && row.ChunkX <= maxX
                    && row.ChunkY >= minY && row.ChunkY <= maxY)
                .ToListAsync().ConfigureAwait(false);

            foreach (ImageChunkRecord row in rows)
            {
                var coord = new ImageChunkCoord(row.ChunkX, row.ChunkY);
                if (!wanted.Contains(coord))
                {
                    continue;
                }

                byte[] pixels = ImageChunkCodec.Decode(row.Format, row.Pixels, image.ChunkSize);
                result.Add((image, coord, pixels));
            }
        }

        return result;
    }

    private void ApplyCompletedLoad()
    {
        if (_pendingLoad is not { IsCompleted: true })
        {
            return;
        }

        Task<List<(PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)>> load = _pendingLoad;
        _pendingLoad = null;

        if (!load.IsCompletedSuccessfully)
        {
            GD.PushError($"[ImageResidency] Load failed: {load.Exception?.GetBaseException().Message}");
            return;
        }

        bool any = false;
        foreach (IGrouping<PaintImage, (PaintImage Image, ImageChunkCoord Coord, byte[] Pixels)> group in load.Result.GroupBy(entry => entry.Image))
        {
            group.Key.PublishLoadedChunks(group.Select(entry => (entry.Coord, entry.Pixels)));
            any = true;
        }

        if (any)
        {
            // Terrain that was sampling zeros over these chunks while they were evicted needs a
            // rebuild now that real pixels are resident again — the same mechanism any other edit uses.
            _context.Streaming.Invalidate();
        }
    }
}
