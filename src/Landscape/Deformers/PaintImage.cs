using System;
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A named, saved raster — width, height, and a painted grayscale buffer — backed internally by a
/// sparse grid of fixed-size <see cref="ImageChunk"/>s rather than one flat byte array. Catalog-backed
/// like <see cref="ProceduralModel"/>, so an <see cref="ImageComponent"/> merely references one by id
/// instead of owning the data: many placements can share one image, and painting it from any of them
/// updates every placement.
///
/// Every pixel-level member (<see cref="Width"/>, <see cref="Height"/>, <see cref="Paint"/>,
/// <see cref="CopyPixels"/>, ...) still behaves as it would over a flat buffer, and the canvas is still
/// capped small enough that a dense copy (used by <see cref="CopyPixels"/>, <see cref="Resize"/>,
/// <see cref="ReplacePixels"/>, <see cref="LoadPixels"/>) is cheap. What chunking already buys: a
/// chunk holding nothing but zeros is never allocated in memory and never gets a row in
/// <see cref="PaintImageFactory"/>'s storage, and a stroke crossing a chunk boundary evaluates its
/// falloff from global pixel coordinates rather than chunk-local ones, so there is nothing to seam.
/// Streamed residency — loading only the chunks a viewport actually needs, which is what lets the
/// canvas grow far past this cap — is a later phase; see <c>.godot/ImageChunkPlan.md</c>.
///
/// Named <c>PaintImage</c> rather than the more obvious <c>Image</c> because this type lives in the
/// same namespace as, and every file here brings in with <c>using Godot;</c>, Godot's own
/// <see cref="Godot.Image"/> — a bare <c>Image</c> here would silently shadow it everywhere.
/// </summary>
public sealed class PaintImage : CatalogEntity, IKeyedCatalogEntity
{
    private const int MinDimension = 1;
    private const int MaxDimension = 4096;
    private const int MinChunkSize = 16;
    private const int MaxChunkSize = 4096;

    private string _name = "Image";
    private int _width = 256;
    private int _height = 256;
    private int _chunkSize = 256;
    private int _chunksX = 1;
    private int _chunksY = 1;
    private Dictionary<ImageChunkCoord, ImageChunk> _chunks = [];

    // What PaintImageFactory last wrote to (or read from) the image_chunks table, kept up to date by
    // its Stage/LoadChunks rather than recomputed from a query — the same "IsSaved" bookkeeping shape
    // every other catalog entity here uses, just per-chunk. Lets Stage tell "still there, needs an
    // update" from "gone since last commit, needs a delete" without a synchronous read of its own.
    private readonly HashSet<ImageChunkCoord> _persistedChunkCoords = [];

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            Revision++;
        }
    }

    public int Width => _width;

    public int Height => _height;

    /// <summary>The fixed tile size chunks are stored and streamed at. See <see cref="ConfigureNew"/>
    /// for why this can only be set on a fresh image.</summary>
    public int ChunkSize => _chunkSize;

    /// <summary>Coordinates of chunks that currently hold at least one non-zero pixel. A coordinate not
    /// in this set is all-zero — see <see cref="ImageChunkTable"/>.</summary>
    public IEnumerable<ImageChunkCoord> ChunkCoords => _chunks.Keys;

    public int ChunkCount => _chunks.Count;

    /// <summary>A dense copy of the whole canvas, gathered from every chunk (absent chunks read as
    /// zero). Cheap only because this phase caps the canvas at <see cref="MaxDimension"/> per axis —
    /// see the type doc.</summary>
    public ReadOnlySpan<byte> Pixels => CopyPixels();

    /// <summary>Bumped by every mutation. A plain counter rather than a content hash — unlike
    /// <see cref="ProceduralModel.NetworkFingerprint"/>, an image's buffer can be megabytes, and a
    /// paint stroke replaces it every frame while dragging, so hashing the bytes on every change
    /// would be far too costly to pay continuously. Identity plus this counter is enough to notice
    /// "this image changed since I last looked" without needing to know how.
    /// </summary>
    public int Revision { get; private set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Sets canvas size and chunk size on a freshly created image — meant to run once, right
    /// after construction, before anything is painted or loaded. Chunk size is not re-configurable
    /// once an image carries real content: rechunking existing pixels is a full rebuild of every
    /// chunk, which the design plan defers as an explicit, separate operation rather than something
    /// that happens implicitly.</summary>
    public void ConfigureNew(int width, int height, int chunkSize)
    {
        _width = Math.Clamp(width, MinDimension, MaxDimension);
        _height = Math.Clamp(height, MinDimension, MaxDimension);
        _chunkSize = Math.Clamp(chunkSize, MinChunkSize, MaxChunkSize);
        RecomputeGrid();
        _chunks = [];
        _persistedChunkCoords.Clear();
        Revision++;
    }

    /// <summary>What <see cref="PaintImageFactory"/> last wrote to (or loaded from) storage — the set
    /// <see cref="Stage"/>-equivalent logic diffs the live chunk set against to know which rows need an
    /// update versus an insert, and which need deleting because a chunk emptied out or the canvas
    /// shrank past them.</summary>
    internal IReadOnlyCollection<ImageChunkCoord> PersistedChunkCoords => _persistedChunkCoords;

    internal void SetPersistedChunkCoords(IEnumerable<ImageChunkCoord> coords)
    {
        _persistedChunkCoords.Clear();
        _persistedChunkCoords.UnionWith(coords);
    }

    /// <summary>Raw bytes for one currently-resident chunk, or null if that coordinate is absent
    /// (all-zero). For storage staging — everything else goes through <see cref="Paint"/>,
    /// <see cref="CopyPixels"/>, or <see cref="CreateSampler"/>.</summary>
    internal byte[]? CopyChunkBytes(ImageChunkCoord coord) =>
        _chunks.TryGetValue(coord, out ImageChunk? chunk) ? chunk.Pixels : null;

    /// <summary>Replaces every chunk from already-decoded storage rows, without dense reconstruction —
    /// the load-time counterpart to <see cref="CreateSampler"/>'s write-side snapshot. Marks every
    /// loaded coordinate as persisted and does not bump <see cref="Revision"/>: this establishes the
    /// loaded state rather than editing it.</summary>
    internal void LoadChunks(IEnumerable<(ImageChunkCoord Coord, byte[] Pixels)> chunks)
    {
        var table = new Dictionary<ImageChunkCoord, ImageChunk>();
        foreach ((ImageChunkCoord coord, byte[] pixels) in chunks)
        {
            table[coord] = new ImageChunk(pixels);
        }

        _chunks = table;
        SetPersistedChunkCoords(table.Keys);
    }

    public byte[] CopyPixels()
    {
        var dense = new byte[_width * _height];
        foreach ((ImageChunkCoord coord, ImageChunk chunk) in _chunks)
        {
            CopyChunkInto(dense, coord, chunk.Pixels);
        }

        return dense;
    }

    /// <summary>Replaces the pixel buffer wholesale, keeping the current resolution. Falls back to a
    /// blank buffer if the given data does not match <see cref="Width"/> x <see cref="Height"/>.</summary>
    public void ReplacePixels(byte[] pixels)
    {
        byte[] dense = pixels.Length == _width * _height ? pixels : new byte[_width * _height];
        RebuildChunks(dense);
        Revision++;
    }

    /// <summary>Changes resolution, bilinear-resampling the existing content into the new size.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Clamp(width, MinDimension, MaxDimension);
        height = Math.Clamp(height, MinDimension, MaxDimension);
        if (width == _width && height == _height)
        {
            return;
        }

        byte[] old = CopyPixels();
        int oldWidth = _width;
        int oldHeight = _height;

        var resized = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int oldY = Math.Clamp((int)((y + 0.5f) * oldHeight / height), 0, oldHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int oldX = Math.Clamp((int)((x + 0.5f) * oldWidth / width), 0, oldWidth - 1);
                resized[(y * width) + x] = old[(oldY * oldWidth) + oldX];
            }
        }

        _width = width;
        _height = height;
        RecomputeGrid();
        RebuildChunks(resized);
        Revision++;
    }

    /// <summary>Replaces both resolution and content at once — the shape a persistence load and an
    /// undo apply/revert both need, as opposed to <see cref="Resize"/>'s resampling.</summary>
    public void LoadPixels(int width, int height, byte[] pixels)
    {
        _width = Math.Clamp(width, MinDimension, MaxDimension);
        _height = Math.Clamp(height, MinDimension, MaxDimension);
        RecomputeGrid();
        byte[] dense = pixels.Length == _width * _height ? pixels : new byte[_width * _height];
        RebuildChunks(dense);
        Revision++;
    }

    /// <summary>Stamps a soft circular brush centred at normalized UV coordinates, with the brush
    /// radius given in the same normalized units along each axis (a caller with a non-square world
    /// footprint passes different radii per axis so the brush reads as round in world space).
    ///
    /// Every weight is evaluated from the pixel's <em>global</em> centre, never a chunk-local one, so
    /// a stroke that straddles a chunk boundary paints identically to the same stroke on an
    /// unchunked image — there is nothing to blend at the seam because there is no seam in the maths,
    /// only in how the result happens to be stored.</summary>
    public bool Paint(float u, float v, float radiusU, float radiusV, float opacity, bool erase)
    {
        if (radiusU <= 0.0f || radiusV <= 0.0f)
        {
            return false;
        }

        int minX = Math.Clamp(Mathf.FloorToInt((u - radiusU) * _width), 0, _width - 1);
        int maxX = Math.Clamp(Mathf.CeilToInt((u + radiusU) * _width), 0, _width - 1);
        int minY = Math.Clamp(Mathf.FloorToInt((v - radiusV) * _height), 0, _height - 1);
        int maxY = Math.Clamp(Mathf.CeilToInt((v + radiusV) * _height), 0, _height - 1);
        byte amount = (byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp(opacity, 0.0f, 1.0f) * 255.0f), 0, 255);
        bool changed = false;

        int chunkMinX = minX / _chunkSize;
        int chunkMaxX = maxX / _chunkSize;
        int chunkMinY = minY / _chunkSize;
        int chunkMaxY = maxY / _chunkSize;

        for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
        {
            for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                if (PaintChunk(new ImageChunkCoord(cx, cy), minX, maxX, minY, maxY, u, v, radiusU, radiusV, amount, erase))
                {
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Revision++;
        }

        return changed;
    }

    /// <summary>Snapshots the current chunk set for a landscape build to sample from — typically off
    /// the main thread while this image may keep being painted on it. See <see cref="ImageChunkTable"/>
    /// for why a snapshot rather than a live view.</summary>
    public ImageSampler CreateSampler() => new(new ImageChunkTable(new Dictionary<ImageChunkCoord, ImageChunk>(_chunks)), _width, _height, _chunkSize);

    private void RecomputeGrid()
    {
        _chunksX = (_width + _chunkSize - 1) / _chunkSize;
        _chunksY = (_height + _chunkSize - 1) / _chunkSize;
    }

    private void RebuildChunks(byte[] dense)
    {
        var chunks = new Dictionary<ImageChunkCoord, ImageChunk>();
        for (int cy = 0; cy < _chunksY; cy++)
        {
            for (int cx = 0; cx < _chunksX; cx++)
            {
                if (ExtractChunk(dense, cx, cy) is { } pixels)
                {
                    chunks[new ImageChunkCoord(cx, cy)] = new ImageChunk(pixels);
                }
            }
        }

        _chunks = chunks;
    }

    /// <summary>Gathers one chunk's worth of pixels out of a dense buffer, or null if every pixel in
    /// its (canvas-clipped) footprint is zero — the source of the "absent chunk means all-zero"
    /// invariant every other member relies on.</summary>
    private byte[]? ExtractChunk(byte[] dense, int cx, int cy)
    {
        var pixels = new byte[_chunkSize * _chunkSize];
        int baseX = cx * _chunkSize;
        int baseY = cy * _chunkSize;
        int width = Math.Min(_chunkSize, _width - baseX);
        int height = Math.Min(_chunkSize, _height - baseY);
        bool any = false;

        for (int y = 0; y < height; y++)
        {
            int denseRow = ((baseY + y) * _width) + baseX;
            int localRow = y * _chunkSize;
            for (int x = 0; x < width; x++)
            {
                byte value = dense[denseRow + x];
                pixels[localRow + x] = value;
                any |= value != 0;
            }
        }

        return any ? pixels : null;
    }

    private void CopyChunkInto(byte[] dense, ImageChunkCoord coord, byte[] pixels)
    {
        int baseX = coord.X * _chunkSize;
        int baseY = coord.Y * _chunkSize;
        int width = Math.Min(_chunkSize, _width - baseX);
        int height = Math.Min(_chunkSize, _height - baseY);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (int y = 0; y < height; y++)
        {
            int denseRow = ((baseY + y) * _width) + baseX;
            int localRow = y * _chunkSize;
            Array.Copy(pixels, localRow, dense, denseRow, width);
        }
    }

    /// <summary>Paints the part of one brush stamp that falls in one chunk, materializing it on first
    /// write and leaving it absent if erasing would have no effect on a chunk that does not exist yet.</summary>
    private bool PaintChunk(ImageChunkCoord coord, int minX, int maxX, int minY, int maxY, float u, float v, float radiusU, float radiusV, byte amount, bool erase)
    {
        int chunkBaseX = coord.X * _chunkSize;
        int chunkBaseY = coord.Y * _chunkSize;
        int loX = Math.Max(minX, chunkBaseX);
        int hiX = Math.Min(maxX, chunkBaseX + _chunkSize - 1);
        int loY = Math.Max(minY, chunkBaseY);
        int hiY = Math.Min(maxY, chunkBaseY + _chunkSize - 1);
        if (loX > hiX || loY > hiY)
        {
            return false;
        }

        byte[]? pixels = null;
        bool changed = false;

        for (int py = loY; py <= hiY; py++)
        {
            float cy = (py + 0.5f) / _height;
            float dy = (cy - v) / radiusV;
            for (int px = loX; px <= hiX; px++)
            {
                float cx = (px + 0.5f) / _width;
                float dx = (cx - u) / radiusU;
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                if (distance > 1.0f)
                {
                    continue;
                }

                float weight = Mathf.SmoothStep(0.0f, 1.0f, 1.0f - distance);
                int delta = Mathf.RoundToInt(amount * weight);
                if (delta == 0)
                {
                    continue;
                }

                if (pixels == null && !TryGetOrCreateChunk(coord, out pixels))
                {
                    // Erasing a chunk that does not exist changes nothing — there is nothing left to
                    // paint in this chunk's share of the stamp.
                    return changed;
                }

                int localX = px - chunkBaseX;
                int localY = py - chunkBaseY;
                int index = (localY * _chunkSize) + localX;
                byte before = pixels![index];
                byte after = erase
                    ? (byte)Math.Max(0, before - delta)
                    : (byte)Math.Min(255, before + delta);

                if (after != before)
                {
                    pixels[index] = after;
                    changed = true;
                }
            }
        }

        return changed;

        bool TryGetOrCreateChunk(ImageChunkCoord chunkCoord, out byte[]? chunkPixels)
        {
            if (_chunks.TryGetValue(chunkCoord, out ImageChunk? existing))
            {
                chunkPixels = existing.Pixels;
                return true;
            }

            if (erase)
            {
                chunkPixels = null;
                return false;
            }

            chunkPixels = new byte[_chunkSize * _chunkSize];
            _chunks[chunkCoord] = new ImageChunk(chunkPixels);
            return true;
        }
    }
}
