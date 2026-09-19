using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A named, saved raster — width, height, and a painted grayscale buffer — backed by a sparse grid of
/// fixed-size <see cref="ImageChunk"/>s rather than one flat byte array. Catalog-backed like
/// <see cref="ProceduralModel"/>, so an <see cref="ImageComponent"/> references one by id instead of
/// owning the data: many placements can share one image, and painting it from any of them updates
/// every placement.
///
/// A chunk holding nothing but zeros is never allocated in memory and never gets a row in
/// <see cref="PaintImageFactory"/>'s storage; only what a streamed-in placement's footprint needs is
/// ever loaded (<see cref="ImageResidencySystem"/>), so <see cref="Width"/> and <see cref="Height"/> can
/// reach <see cref="MaxDimension"/> without every image needing that much memory at once. A stroke
/// crossing a chunk boundary evaluates its falloff from global pixel coordinates, so there is nothing
/// to seam.
///
/// <see cref="CopyPixels"/>, <see cref="Resize"/>, <see cref="ReplacePixels"/> and
/// <see cref="LoadPixels"/> are the exception: they work over one dense buffer sized to the whole
/// canvas, for callers that need that shape (tests, an external import). They scale with
/// <see cref="Width"/> × <see cref="Height"/>, so they are only safe on a modestly sized image. Width,
/// height, and chunk size are fixed for an image's lifetime, chosen once via
/// <see cref="ConfigureNew"/>.
///
/// Named <c>PaintImage</c> rather than <c>Image</c> because a bare <c>Image</c> would shadow Godot's
/// <see cref="Godot.Image"/> in every file that has <c>using Godot;</c>.
/// </summary>
public sealed class PaintImage : CatalogEntity, IKeyedCatalogEntity
{
    /// <summary>The largest <see cref="Width"/> or <see cref="Height"/> <see cref="ConfigureNew"/>
    /// will accept — public so a caller building its own size UI (the creation form) can clamp or
    /// display against the same limit rather than duplicating the number.</summary>
    public const int MaxDimension = 1_048_576;

    /// <summary>Default <see cref="DiskTilePattern"/> — <c>{x}</c>/<c>{y}</c> are replaced with a
    /// chunk's grid coordinate. Public so the creation form can show and pre-fill the same string.</summary>
    public const string DefaultDiskTilePattern = "{x}_{y}.png";

    private const int MinDimension = 1;
    private const int MinChunkSize = 16;
    private const int MaxChunkSize = 4096;

    private string _name = "Image";
    private int _width = 256;
    private int _height = 256;
    private int _chunkSize = 256;
    private int _components = 1;
    private PaintImagePixelFormat _format = PaintImagePixelFormat.Byte;
    private int _chunksX = 1;
    private int _chunksY = 1;
    private Dictionary<ImageChunkCoord, ImageChunk> _chunks = [];

    private PaintImageStorageKind _storageKind = PaintImageStorageKind.Database;
    private string _diskPath = "";
    private string _diskTilePattern = DefaultDiskTilePattern;

    // What PaintImageFactory last wrote to (or read from) the image_chunks table, kept up to date by
    // its Stage/LoadChunks rather than recomputed from a query — the same "IsSaved" bookkeeping shape
    // every other catalog entity here uses, just per-chunk. Lets Stage tell "still there, needs an
    // update" from "gone since last commit, needs a delete" without a synchronous read of its own.
    private readonly HashSet<ImageChunkCoord> _persistedChunkCoords = [];

    // Coordinates an edit has explicitly emptied out (erased to all-zero, or dropped by a canvas
    // resize/clear) since the last commit, kept separate from "not currently resident" on purpose:
    // a chunk merely evicted by ImageResidencySystem is not gone, just not in memory right now, and
    // must never cause Stage to delete its row. Only a coordinate in this set does.
    private readonly HashSet<ImageChunkCoord> _removedSincePersist = [];

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
            MarkDirtyAll();
            BumpContent();
        }
    }

    public int Width => _width;

    public int Height => _height;

    /// <summary>The fixed tile size chunks are stored and streamed at. See <see cref="ConfigureNew"/>
    /// for why this can only be set on a fresh image.</summary>
    public int ChunkSize => _chunkSize;

    /// <summary>Channels stored per pixel: 1 for a scalar mask, 3 for RGB, 4 for RGBA — interleaved per
    /// pixel the same way <see cref="LandscapeChannel.Components"/> interleaves per texel. Fixed for
    /// an image's whole lifetime like <see cref="Width"/>/<see cref="Height"/>/<see cref="ChunkSize"/>,
    /// set once via <see cref="ConfigureNew"/>.</summary>
    public int Components => _components;

    /// <summary>How each channel is stored — see <see cref="PaintImagePixelFormat"/>. Fixed for an
    /// image's whole lifetime like <see cref="Components"/>, set once via <see cref="ConfigureNew"/>.</summary>
    public PaintImagePixelFormat Format => _format;

    /// <summary>Bytes per channel: 1 for <see cref="PaintImagePixelFormat.Byte"/>, 4 for
    /// <see cref="PaintImagePixelFormat.Float32"/>.</summary>
    public int ElementSize => _format.ElementSize();

    /// <summary>Bytes stored per pixel — <see cref="Components"/> × <see cref="ElementSize"/>. What a
    /// dense buffer or a chunk's fixed tile actually scales with; <see cref="Components"/> alone only
    /// ever did before <see cref="PaintImagePixelFormat.Float32"/> existed, when every channel was
    /// exactly one byte.</summary>
    public int Stride => _components * ElementSize;

    /// <summary>Where this image's chunk pixels are stored — see <see cref="PaintImageStorageKind"/>.
    /// Set once at creation via <see cref="ConfigureDiskSource"/> (or left <see cref="PaintImageStorageKind.Database"/>).</summary>
    public PaintImageStorageKind StorageKind => _storageKind;

    /// <summary>Whether this image reads and writes its pixels as disk files rather than storage rows.</summary>
    public bool IsDiskBacked => _storageKind == PaintImageStorageKind.Disk;

    /// <summary>A disk-backed image's absolute filesystem path — the image file itself when the grid is
    /// a single chunk, otherwise the directory holding the tile files. Empty for a database-backed
    /// image.</summary>
    [ScriptProperty]
    public string DiskPath => _diskPath;

    /// <summary>Tile file name pattern for a multi-chunk disk-backed image — <c>{x}</c>/<c>{y}</c> are
    /// replaced with the chunk's grid coordinate. Unused for a single-chunk or database-backed image.</summary>
    public string DiskTilePattern => _diskTilePattern;

    /// <summary>Whether a disk-backed image stores one tile file per chunk (grid larger than 1x1)
    /// rather than a single image file.</summary>
    public bool IsTiledDisk => IsDiskBacked && (_chunksX > 1 || _chunksY > 1);

    /// <summary>The image file extension a disk-backed image reads and writes — <c>.exr</c> for a
    /// <see cref="PaintImagePixelFormat.Float32"/> image (PNG has no float pixel format), <c>.png</c>
    /// otherwise.</summary>
    public string DiskExtension => _format == PaintImagePixelFormat.Float32 ? ".exr" : ".png";

    /// <summary>The name of <see cref="StorageKind"/>, for a script or a diagnostic readout.</summary>
    [ScriptProperty]
    public string StorageKindName => _storageKind.ToString();

    /// <summary>The chunk grid's extent — every valid chunk coordinate's X falls in <c>[0, ChunksX)</c>,
    /// Y in <c>[0, ChunksY)</c>. The last column/row typically only partly overlaps the canvas, when
    /// <see cref="ChunkSize"/> does not evenly divide <see cref="Width"/>/<see cref="Height"/>.</summary>
    public int ChunksX => _chunksX;

    public int ChunksY => _chunksY;

    /// <summary>Coordinates of chunks that currently hold at least one non-zero pixel. A coordinate not
    /// in this set is all-zero — see <see cref="ImageChunkTable"/>.</summary>
    public IEnumerable<ImageChunkCoord> ChunkCoords => _chunks.Keys;

    public int ChunkCount => _chunks.Count;

    /// <summary>A dense copy of the whole canvas, gathered from every <em>resident</em> chunk (absent
    /// or non-resident chunks read as zero). Scales with <see cref="Width"/> × <see cref="Height"/>
    /// regardless of how much is actually resident — see the type doc for why this is only safe on a
    /// modestly sized image, and not what a large image's own display or export path uses.</summary>
    public ReadOnlySpan<byte> Pixels => CopyPixels();

    /// <summary>Bumped only by an actual pixel/name/dimension edit — what export dirtiness and
    /// <see cref="ImageComponent.ContentVersion"/> key off, so a chunk merely streaming in or out
    /// (which <see cref="ViewRevision"/> also tracks) never marks terrain dirty or triggers a
    /// re-export on ground nobody touched.</summary>
    public int ContentRevision { get; private set; }

    /// <summary>Bumped by everything <see cref="ContentRevision"/> is, plus a chunk becoming resident
    /// or getting evicted — what a viewport representation (a decal, a paintable quad, a picker
    /// preview) should rebuild against, since streamed-in pixels need to be seen even though they are
    /// not new content.
    ///
    /// A plain counter rather than a content hash — unlike <see cref="ProceduralModel.NetworkFingerprint"/>,
    /// an image's buffer can be megabytes, and a paint stroke replaces it every frame while dragging,
    /// so hashing the bytes on every change would be far too costly to pay continuously. Identity plus
    /// this counter is enough to notice "this image changed since I last looked" without needing to
    /// know how.</summary>
    public int ViewRevision { get; private set; }

    // A bounded log of recently dirtied pixel rects, each tagged with a monotonically increasing
    // sequence number. Deliberately not a single "accumulate then clear" rect: several
    // ImageComponents can share one image (see the type doc), and each needs to consume the same
    // edits independently, so clearing on the first consumer would starve every other placement.
    //
    // Pixel rects rather than chunk coordinates because chunk granularity is far too coarse to be
    // useful — a default 256x256 image is a single 256px chunk, so "the chunks this stroke touched"
    // is the whole canvas, and a placement stretched over a large footprint would rebuild every
    // landscape chunk beneath it for one brush dab.
    private readonly List<(long Seq, int MinX, int MinY, int MaxX, int MaxY)> _dirtyLog = [];
    private long _dirtySeq;
    private long _dirtyDroppedThrough = -1;
    private const int MaxDirtyLog = 64;

    /// <summary>The newest dirty-log sequence number. A consumer records this after reading, and
    /// passes it back to <see cref="TryDirtyPixelsSince"/> next time to get only what changed since.</summary>
    internal long DirtySequence => _dirtySeq;

    /// <summary>Records that a pixel rect changed. Inclusive bounds, clamped to the canvas.</summary>
    private void MarkDirtyPixels(int minX, int minY, int maxX, int maxY)
    {
        minX = Math.Clamp(minX, 0, _width - 1);
        maxX = Math.Clamp(maxX, 0, _width - 1);
        minY = Math.Clamp(minY, 0, _height - 1);
        maxY = Math.Clamp(maxY, 0, _height - 1);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        _dirtyLog.Add((++_dirtySeq, minX, minY, maxX, maxY));

        // Oldest entries fall off rather than growing without bound. A consumer that was behind the
        // dropped entries can no longer reconstruct what it missed, which TryDirtyPixelsSince turns
        // into a whole-canvas answer — correct, just not narrow.
        if (_dirtyLog.Count > MaxDirtyLog)
        {
            _dirtyDroppedThrough = _dirtyLog[0].Seq;
            _dirtyLog.RemoveAt(0);
        }
    }

    private void MarkDirtyAll() => MarkDirtyPixels(0, 0, _width - 1, _height - 1);

    private void MarkDirtyChunk(ImageChunkCoord coord) => MarkDirtyPixels(
        coord.X * _chunkSize,
        coord.Y * _chunkSize,
        ((coord.X + 1) * _chunkSize) - 1,
        ((coord.Y + 1) * _chunkSize) - 1);

    /// <summary>The union of every pixel rect dirtied after <paramref name="since"/>, or false when
    /// nothing has changed. Falls back to the whole canvas when <paramref name="since"/> is older than
    /// what the log still holds — over-reporting is merely slow, under-reporting leaves stale terrain.</summary>
    internal bool TryDirtyPixelsSince(long since, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = int.MaxValue;
        maxX = maxY = int.MinValue;

        if (since >= _dirtySeq)
        {
            return false;
        }

        if (since < _dirtyDroppedThrough)
        {
            minX = 0;
            minY = 0;
            maxX = _width - 1;
            maxY = _height - 1;
            return true;
        }

        bool any = false;
        foreach ((long seq, int lo0, int lo1, int hi0, int hi1) in _dirtyLog)
        {
            if (seq <= since)
            {
                continue;
            }

            minX = Math.Min(minX, lo0);
            minY = Math.Min(minY, lo1);
            maxX = Math.Max(maxX, hi0);
            maxY = Math.Max(maxY, hi1);
            any = true;
        }

        return any;
    }

    private void BumpContent()
    {
        ContentRevision++;
        ViewRevision++;
    }

    private void BumpView() => ViewRevision++;

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    /// <summary>Sets canvas size and chunk size on a freshly created image — meant to run once, right
    /// after construction, before anything is painted or loaded. None of the three are re-configurable
    /// once an image carries real content: a canvas resize has to decide what happens to chunks the new
    /// bounds no longer cover, and a re-chunk is a full rebuild of every chunk — both are surprising
    /// things for an "edit a field" action to trigger on an image that may already be painted, shared
    /// across placements, and partially committed, so this editor does not offer either. Pick the size
    /// you want at creation.</summary>
    public void ConfigureNew(int width, int height, int chunkSize, int components = 1, PaintImagePixelFormat format = PaintImagePixelFormat.Byte)
    {
        _width = Math.Clamp(width, MinDimension, MaxDimension);
        _height = Math.Clamp(height, MinDimension, MaxDimension);
        _chunkSize = Math.Clamp(chunkSize, MinChunkSize, MaxChunkSize);
        _components = components is 1 or 3 or 4 ? components : 1;

        // Float32 is only meaningful on a scalar channel — a color paint/lerp is defined entirely in
        // terms of a byte's [0,255] saturation (see PaintChunkColor), so a 3/4-component image simply
        // never gets offered anything but Byte.
        _format = format == PaintImagePixelFormat.Float32 && _components != 1 ? PaintImagePixelFormat.Byte : format;
        RecomputeGrid();
        _chunks = [];
        MarkDirtyAll();

        // Deliberately not clearing _persistedChunkCoords: on every current caller it is already empty
        // (a freshly constructed PaintImage, not yet staged), so this is a no-op today. But a future
        // "reconfigure an existing, already-committed image" caller needs the old manifest intact for
        // Stage to still know which now-orphaned rows to delete — clearing it here would silently leak
        // those rows forever.
        BumpContent();
    }

    /// <summary>Switches this image to <see cref="PaintImageStorageKind.Disk"/>, backed by files at
    /// <paramref name="path"/> (an absolute filesystem path). Call right after <see cref="ConfigureNew"/>
    /// at creation, and from the loader when rehydrating a stored disk-backed image. <paramref name="path"/>
    /// is the image file for a single-chunk grid, the directory holding the tile files otherwise;
    /// <paramref name="tilePattern"/> names each tile (see <see cref="DiskTilePattern"/>).</summary>
    public void ConfigureDiskSource(string path, string tilePattern)
    {
        _storageKind = PaintImageStorageKind.Disk;
        _diskPath = path ?? "";
        _diskTilePattern = string.IsNullOrWhiteSpace(tilePattern) ? DefaultDiskTilePattern : tilePattern;
    }

    /// <summary>The filesystem path for one chunk's file — <see cref="DiskPath"/> itself for a
    /// single-chunk image, otherwise the directory joined with <see cref="DiskTilePattern"/> with
    /// <c>{x}</c>/<c>{y}</c> substituted.</summary>
    public string DiskChunkPath(ImageChunkCoord coord) => IsTiledDisk
        ? System.IO.Path.Combine(_diskPath, ImageDiskStore.TileFileName(_diskTilePattern, coord))
        : _diskPath;

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

    /// <summary>Coordinates explicitly emptied out since the last commit — see the field doc. What
    /// <see cref="PaintImageFactory.Stage"/> deletes rows for, instead of inferring deletions from
    /// "not currently resident" (which eviction would also match).</summary>
    internal IReadOnlyCollection<ImageChunkCoord> RemovedSincePersist => _removedSincePersist;

    /// <summary>Reconciles bookkeeping after a successful commit: <paramref name="upserted"/> joins the
    /// manifest and is marked clean again (free for <see cref="EvictChunk"/> to drop once nothing needs
    /// it resident); <paramref name="deleted"/> leaves both the manifest and
    /// <see cref="RemovedSincePersist"/>. A coordinate that is neither — a stored-but-not-resident
    /// chunk this commit never touched — is left exactly as it was.</summary>
    internal void CommitChunkPersistence(IReadOnlyCollection<ImageChunkCoord> upserted, IReadOnlyCollection<ImageChunkCoord> deleted)
    {
        _persistedChunkCoords.ExceptWith(deleted);
        _persistedChunkCoords.UnionWith(upserted);

        foreach (ImageChunkCoord coord in deleted)
        {
            _removedSincePersist.Remove(coord);
        }

        MarkChunksClean(upserted);
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

    /// <summary>Loads only the coordinate manifest — which chunks exist in storage — without their
    /// pixel data. What lets a catalog load stay cheap for a huge image: <see cref="ImageResidencySystem"/>
    /// fetches actual chunk bytes later, only for what a viewport needs. A coordinate reported here
    /// reads as zero (via <see cref="CreateSampler"/>) and refuses to be painted over (see
    /// <see cref="Paint"/>) until it is actually loaded.</summary>
    internal void LoadManifest(IEnumerable<ImageChunkCoord> coords)
    {
        _chunks = [];
        SetPersistedChunkCoords(coords);
    }

    /// <summary>Whether a coordinate's content genuinely exists in storage as far as this session currently
    /// believes — resident or not. False for a coordinate this session has explicitly emptied out (see
    /// <see cref="RemovedSincePersist"/>) even before that reaches storage, so neither <see cref="Paint"/> nor
    /// <see cref="ImageResidencySystem"/> treat an uncommitted erase as something still worth
    /// reloading.</summary>
    internal bool IsStored(ImageChunkCoord coord) => _persistedChunkCoords.Contains(coord) && !_removedSincePersist.Contains(coord);

    internal bool IsResident(ImageChunkCoord coord) => _chunks.ContainsKey(coord);

    /// <summary>This chunk's content revision, or -1 if it is not resident. See
    /// <see cref="ImageChunk.Revision"/>.</summary>
    public int ChunkRevision(ImageChunkCoord coord) =>
        _chunks.TryGetValue(coord, out ImageChunk? chunk) ? chunk.Revision : -1;

    internal bool IsDirty(ImageChunkCoord coord) => _chunks.TryGetValue(coord, out ImageChunk? chunk) && chunk.Dirty;

    /// <summary>Every chunk's fixed storage footprint — what a residency budget is measured in.</summary>
    internal long ChunkByteSize => (long)_chunkSize * _chunkSize * Stride;

    internal long ResidentByteSize => _chunks.Count * ChunkByteSize;

    /// <summary>Merges freshly loaded chunk bytes into residency as clean (matches storage), returning
    /// the coordinates it actually applied — what a caller needs to mark the terrain sampling them
    /// stale. A coordinate that is already resident is left alone (and left out of the result) — it
    /// raced against a paint or an eviction since the load was requested, and whatever is live now is
    /// more current than what this load saw. Likewise skipped if the coordinate has since been
    /// explicitly emptied out and not yet committed (<see cref="RemovedSincePersist"/>): the load was
    /// requesting what storage held before that erase, and applying it now would silently resurrect
    /// content the user just removed.</summary>
    internal IReadOnlyList<ImageChunkCoord> PublishLoadedChunks(IEnumerable<(ImageChunkCoord Coord, byte[] Pixels)> chunks)
    {
        List<ImageChunkCoord>? applied = null;
        foreach ((ImageChunkCoord coord, byte[] pixels) in chunks)
        {
            if (_chunks.ContainsKey(coord) || _removedSincePersist.Contains(coord))
            {
                continue;
            }

            _chunks[coord] = new ImageChunk(pixels);
            (applied ??= []).Add(coord);
        }

        if (applied is null)
        {
            return [];
        }

        BumpView();
        return applied;
    }

    /// <summary>Drops a clean resident chunk from memory — the pixels stay safe in storage, only the
    /// in-memory copy goes away. A no-op if the chunk is dirty (unsaved edits) or already gone: the
    /// residency system is expected to have already excluded those, but never evicting one is cheap
    /// insurance against ever losing unsaved work to a budget sweep. Returns whether it actually
    /// evicted something — <see cref="ImageResidencySystem"/> uses that to know whether a landscape
    /// chunk that sampled this coordinate while it was resident needs rebuilding now that it reads as
    /// zero again.</summary>
    internal bool EvictChunk(ImageChunkCoord coord)
    {
        if (_chunks.TryGetValue(coord, out ImageChunk? chunk) && !chunk.Dirty && _chunks.Remove(coord))
        {
            BumpView();
            return true;
        }

        return false;
    }

    /// <summary>Clears the dirty flag on every given resident chunk. Called once a commit has
    /// successfully persisted them — see <see cref="PaintImageFactory.Stage"/> — so
    /// <see cref="EvictChunk"/> becomes free to drop them again once nothing needs them resident.
    /// Without this, a chunk that was ever painted would stay pinned in memory forever, since eviction
    /// refuses to touch a dirty one.</summary>
    internal void MarkChunksClean(IEnumerable<ImageChunkCoord> coords)
    {
        foreach (ImageChunkCoord coord in coords)
        {
            if (_chunks.TryGetValue(coord, out ImageChunk? chunk))
            {
                chunk.Dirty = false;
            }
        }
    }

    // Coordinate to its content immediately before the in-progress stroke (null = did not exist), for
    // every chunk the stroke has touched so far. Not the same as PersistedChunkCoords/manifest
    // bookkeeping — this is purely in-memory, scoped to one stroke, and reset on every BeginStroke.
    private Dictionary<ImageChunkCoord, byte[]?>? _strokeBefore;

    // Coordinates a stamp actually altered during the current stroke. EndStroke skips the whole-chunk
    // copy and byte compare for any chunk the brush merely reached into without changing.
    private HashSet<ImageChunkCoord>? _strokeChanged;

    // Reused dx² scratch for PaintChunkFloat, one slot per chunk column. Painting is single-threaded
    // (the paint tool, main thread), so one buffer on the image is enough and costs no per-stamp
    // allocation.
    private float[]? _stampColumnDistSq;

    /// <summary>Starts recording per-chunk "before" snapshots for an undo command. Call once when a
    /// paint stroke begins (e.g. on mouse-down) — not once per <see cref="Paint"/> call, since a single
    /// stroke calls <see cref="Paint"/> many times as the pointer moves and undo needs the state from
    /// before the <em>whole</em> stroke, not before each dab.</summary>
    public void BeginStroke()
    {
        _strokeBefore = [];
        _strokeChanged = [];
    }

    /// <summary>Stops recording and returns every chunk that actually changed since
    /// <see cref="BeginStroke"/>: its coordinate, its content immediately before the stroke (null if it
    /// did not exist yet), and its content now (null if it does not exist now — erased back to empty).
    /// A chunk the stroke merely touched but left unchanged (every dab in it rounded to zero) is
    /// omitted. Safe to call with no stroke in progress — returns empty.</summary>
    public IReadOnlyList<(ImageChunkCoord Coord, byte[]? Before, byte[]? After)> EndStroke()
    {
        if (_strokeBefore is not { } before)
        {
            return [];
        }

        _strokeBefore = null;
        HashSet<ImageChunkCoord>? changed = _strokeChanged;
        _strokeChanged = null;

        var result = new List<(ImageChunkCoord, byte[]?, byte[]?)>();
        foreach ((ImageChunkCoord coord, byte[]? beforePixels) in before)
        {
            // A chunk the brush reached into but never actually altered needs no copy or compare —
            // the byte comparison would only confirm it is unchanged.
            if (changed is not null && !changed.Contains(coord))
            {
                continue;
            }

            byte[]? afterPixels = CopyChunkBytes(coord);
            if (!BytesEqual(beforePixels, afterPixels))
            {
                // Copied, the way the "before" side already is: that is the chunk's live buffer, and a
                // later stroke over the same chunk paints into it in place, which would rewrite this
                // record's "after" content long after the stroke it describes.
                result.Add((coord, beforePixels, (byte[]?)afterPixels?.Clone()));
            }
        }

        return result;
    }

    /// <summary>Applies a set of chunk edits directly by coordinate — a replacement buffer, or null
    /// meaning the chunk should not exist (all-zero) — the shape a per-chunk undo apply/revert needs,
    /// as opposed to <see cref="LoadPixels"/>'s dense whole-canvas replacement. Marks every affected
    /// chunk dirty, so a commit re-persists it on the next <see cref="PaintImageFactory.Stage"/>.
    /// Never refuses a coordinate the way <see cref="Paint"/> does for a stored-but-unloaded chunk:
    /// every edit this is called with came from <see cref="EndStroke"/>, which by construction only
    /// ever recorded chunks <see cref="Paint"/> actually let it touch while they were resident.</summary>
    internal void ApplyChunkEdits(IEnumerable<(ImageChunkCoord Coord, byte[]? Pixels)> edits)
    {
        bool any = false;
        foreach ((ImageChunkCoord coord, byte[]? pixels) in edits)
        {
            // Chunk granularity is all an undo/redo apply has to go on — it replaces whole chunk
            // buffers — so unlike a paint stamp this cannot narrow below one chunk.
            MarkDirtyChunk(coord);

            if (pixels == null)
            {
                any |= _chunks.Remove(coord);
                _removedSincePersist.Add(coord);
            }
            else
            {
                // Carries the previous revision forward and bumps it, so a viewport that had this
                // coordinate cached still sees a difference rather than a coincidental match against
                // a fresh chunk's zero.
                int revision = _chunks.TryGetValue(coord, out ImageChunk? previous) ? previous.Revision + 1 : 0;
                _chunks[coord] = new ImageChunk((byte[])pixels.Clone()) { Dirty = true, Revision = revision };
                _removedSincePersist.Remove(coord);
                any = true;
            }
        }

        if (any)
        {
            BumpContent();
        }
    }

    private static bool BytesEqual(byte[]? a, byte[]? b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.AsSpan().SequenceEqual(b);
    }

    private static bool IsAllZero(byte[] pixels) => !pixels.AsSpan().ContainsAnyExcept((byte)0);

    public byte[] CopyPixels()
    {
        var dense = new byte[_width * _height * Stride];
        foreach ((ImageChunkCoord coord, ImageChunk chunk) in _chunks)
        {
            CopyChunkInto(dense, coord, chunk.Pixels);
        }

        return dense;
    }

    /// <summary>Replaces the pixel buffer wholesale, keeping the current resolution. Falls back to a
    /// blank buffer if the given data does not match <see cref="Width"/> x <see cref="Height"/> x
    /// <see cref="Components"/>.</summary>
    public void ReplacePixels(byte[] pixels)
    {
        int expected = _width * _height * Stride;
        byte[] dense = pixels.Length == expected ? pixels : new byte[expected];
        RebuildChunks(dense);
        MarkDirtyAll();
        BumpContent();
    }

    /// <summary>Changes resolution, bilinear-resampling the existing content into the new size. Dense —
    /// allocates a buffer proportional to both the old and new canvas area — so this is only safe on a
    /// canvas small enough for that to be cheap, and not something the editor's own UI offers: see
    /// <see cref="ConfigureNew"/> for why canvas size is creation-only there.</summary>
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
        int stride = Stride;

        var resized = new byte[width * height * stride];
        for (int y = 0; y < height; y++)
        {
            int oldY = Math.Clamp((int)((y + 0.5f) * oldHeight / height), 0, oldHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int oldX = Math.Clamp((int)((x + 0.5f) * oldWidth / width), 0, oldWidth - 1);
                int sourceIndex = ((oldY * oldWidth) + oldX) * stride;
                int targetIndex = ((y * width) + x) * stride;
                Array.Copy(old, sourceIndex, resized, targetIndex, stride);
            }
        }

        _width = width;
        _height = height;
        RecomputeGrid();
        RebuildChunks(resized);
        MarkDirtyAll();
        BumpContent();
    }

    /// <summary>Replaces both resolution and content at once — the shape a persistence load and an
    /// undo apply/revert both need, as opposed to <see cref="Resize"/>'s resampling.</summary>
    public void LoadPixels(int width, int height, byte[] pixels)
    {
        _width = Math.Clamp(width, MinDimension, MaxDimension);
        _height = Math.Clamp(height, MinDimension, MaxDimension);
        RecomputeGrid();
        int expected = _width * _height * Stride;
        byte[] dense = pixels.Length == expected ? pixels : new byte[expected];
        RebuildChunks(dense);
        MarkDirtyAll();
        BumpContent();
    }

    /// <summary>Drops every currently <em>resident</em> chunk. Chunk-based, so unlike
    /// <see cref="ReplacePixels"/> with a zeroed buffer this stays cheap regardless of canvas size — but
    /// that means it is not necessarily a true whole-canvas wipe: a chunk that is stored but not
    /// resident (evicted, or simply never loaded this session on a canvas too large to ever be fully
    /// resident) is left untouched in storage. Returned chunks are what a caller builds undo around —
    /// see <c>PaintImageChunksCommand</c>, which this reuses directly since "every dropped chunk goes to
    /// null" is exactly the shape it already understands.</summary>
    public IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> ClearAll()
    {
        if (_chunks.Count == 0)
        {
            return [];
        }

        var cleared = new List<(ImageChunkCoord, byte[])>();
        foreach ((ImageChunkCoord coord, ImageChunk chunk) in _chunks)
        {
            cleared.Add((coord, chunk.Pixels));
            _removedSincePersist.Add(coord);
            MarkDirtyChunk(coord);
        }

        _chunks = [];
        BumpContent();
        return cleared;
    }

    /// <summary>Stamps a soft circular brush centred at normalized UV coordinates, with the brush
    /// radius given in the same normalized units along each axis (a caller with a non-square world
    /// footprint passes different radii per axis so the brush reads as round in world space).
    ///
    /// Every weight is evaluated from the pixel's <em>global</em> centre, never a chunk-local one, so
    /// a stroke that straddles a chunk boundary paints identically to the same stroke on an
    /// unchunked image — there is nothing to blend at the seam because there is no seam in the maths,
    /// only in how the result happens to be stored.
    ///
    /// Touches only a pixel's first component. Exactly what a scalar (<see cref="Components"/> == 1)
    /// image's only component is — for a color image, use the <see cref="Color"/> overload instead;
    /// this one is kept for a caller (a plain coverage mask) that never has a color to paint with.
    ///
    /// On a <see cref="PaintImagePixelFormat.Float32"/> image this accumulates unclamped rather than
    /// saturating at a byte's [0,255] — the same "not necessarily a [0,1] mask" reasoning
    /// <see cref="ImageComponent.Rasterize"/> already applies to a scalar channel feeding a direct
    /// world-height buffer, just enforced at the storage end too instead of only at the point of
    /// reading it back.</summary>
    public bool Paint(float u, float v, float radiusU, float radiusV, float opacity, bool erase, float hardness = 0.0f)
    {
        if (radiusU <= 0.0f || radiusV <= 0.0f)
        {
            return false;
        }

        int minX = Math.Clamp(Mathf.FloorToInt((u - radiusU) * _width), 0, _width - 1);
        int maxX = Math.Clamp(Mathf.CeilToInt((u + radiusU) * _width), 0, _width - 1);
        int minY = Math.Clamp(Mathf.FloorToInt((v - radiusV) * _height), 0, _height - 1);
        int maxY = Math.Clamp(Mathf.CeilToInt((v + radiusV) * _height), 0, _height - 1);
        bool changed = false;

        int chunkMinX = minX / _chunkSize;
        int chunkMaxX = maxX / _chunkSize;
        int chunkMinY = minY / _chunkSize;
        int chunkMaxY = maxY / _chunkSize;

        if (_format == PaintImagePixelFormat.Float32)
        {
            float amount = Mathf.Clamp(opacity, 0.0f, 1.0f);
            for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
            {
                for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
                {
                    if (PaintChunkFloat(new ImageChunkCoord(cx, cy), minX, maxX, minY, maxY, u, v, radiusU, radiusV, amount, erase, hardness))
                    {
                        changed = true;
                    }
                }
            }
        }
        else
        {
            byte amount = (byte)Math.Clamp(Mathf.RoundToInt(Mathf.Clamp(opacity, 0.0f, 1.0f) * 255.0f), 0, 255);
            for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
            {
                for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
                {
                    if (PaintChunk(new ImageChunkCoord(cx, cy), minX, maxX, minY, maxY, u, v, radiusU, radiusV, amount, erase, hardness))
                    {
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            // The stamp's own pixel bbox, which is as narrow as this gets — the whole point of the
            // dirty log is that this is brush-sized rather than chunk- or canvas-sized.
            MarkDirtyPixels(minX, minY, maxX, maxY);
            BumpContent();
        }

        return changed;
    }

    /// <summary>
    /// Stamps a soft circular brush like the scalar overload, but paints a color: on a 4-component
    /// (RGBA) image, alpha accumulates exactly like the scalar overload's single byte, and RGB lerps
    /// toward <paramref name="color"/> weighted by the alpha actually applied this stamp — so a texel
    /// erased all the way to zero alpha is also zeroed in RGB, keeping "absent chunk means all-zero"
    /// true through an erase. On a 3-component (RGB, no alpha) image there is no coverage channel to
    /// drive that weighting, so the brush's own falloff weight is used directly as the lerp factor,
    /// toward <paramref name="color"/> when painting or toward black when erasing. On a scalar image
    /// this behaves exactly like the scalar overload — <paramref name="color"/> is unused.
    /// </summary>
    public bool Paint(float u, float v, float radiusU, float radiusV, Color color, float opacity, bool erase, float hardness = 0.0f)
    {
        if (_components == 1)
        {
            return Paint(u, v, radiusU, radiusV, opacity, erase, hardness);
        }

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
                if (PaintChunkColor(new ImageChunkCoord(cx, cy), minX, maxX, minY, maxY, u, v, radiusU, radiusV, color, amount, erase, hardness))
                {
                    changed = true;
                }
            }
        }

        if (changed)
        {
            MarkDirtyPixels(minX, minY, maxX, maxY);
            BumpContent();
        }

        return changed;
    }

    /// <summary>Whether a write to <paramref name="coord"/> is safe: the chunk is resident, or stored nowhere
    /// (all zero). A stored chunk that is not resident would be overwritten from a blank buffer.</summary>
    public bool CanEdit(ImageChunkCoord coord) => _chunks.ContainsKey(coord) || !IsStored(coord);

    /// <summary>Whether <see cref="CanEdit"/> holds for every chunk a pixel rectangle touches. False for a
    /// rectangle that is not inside the image.</summary>
    public bool CanEditRegion(int x, int y, int width, int height)
    {
        if (!RegionInside(x, y, width, height))
        {
            return false;
        }

        for (int cy = y / _chunkSize; cy <= (y + height - 1) / _chunkSize; cy++)
        {
            for (int cx = x / _chunkSize; cx <= (x + width - 1) / _chunkSize; cx++)
            {
                if (!CanEdit(new ImageChunkCoord(cx, cy)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Copies out a pixel rectangle inside the image, <see cref="Stride"/> bytes per pixel,
    /// row-major. Absent chunks read as zero.</summary>
    public byte[] ReadPixels(int x, int y, int width, int height)
    {
        if (!RegionInside(x, y, width, height))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The rectangle is not inside the image.");
        }

        int stride = Stride;
        var result = new byte[width * height * stride];
        ForEachRun(x, y, width, height, (coord, chunkOffset, regionOffset, run) =>
        {
            if (_chunks.TryGetValue(coord, out ImageChunk? chunk))
            {
                Array.Copy(chunk.Pixels, chunkOffset * stride, result, regionOffset * stride, run * stride);
            }
        });

        return result;
    }

    /// <summary>
    /// Writes a pixel rectangle inside the image (<see cref="Stride"/> bytes per pixel, row-major) as part of
    /// the stroke begun with <see cref="BeginStroke"/>: chunks are snapshotted before their first change, and
    /// changed pixels are tracked and signalled like a paint dab. Nothing is written when any touched chunk
    /// fails <see cref="CanEdit"/>. True when a pixel changed.
    /// </summary>
    public bool WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> pixels)
    {
        int stride = Stride;
        if (pixels.Length < width * height * stride || !CanEditRegion(x, y, width, height))
        {
            return false;
        }

        var touched = new Dictionary<ImageChunkCoord, bool>();
        byte[] source = pixels.ToArray();
        ForEachRun(x, y, width, height, (coord, chunkOffset, regionOffset, run) =>
        {
            bool resident = _chunks.TryGetValue(coord, out ImageChunk? existing);
            var incoming = new ReadOnlySpan<byte>(source, regionOffset * stride, run * stride);
            if (!resident && !incoming.ContainsAnyExcept((byte)0))
            {
                return;
            }

            byte[] chunkPixels = resident ? existing!.Pixels : new byte[_chunkSize * _chunkSize * stride];
            Span<byte> target = chunkPixels.AsSpan(chunkOffset * stride, run * stride);
            if (target.SequenceEqual(incoming))
            {
                return;
            }

            if (_strokeBefore is { } stroke && !stroke.ContainsKey(coord))
            {
                stroke[coord] = resident ? (byte[])chunkPixels.Clone() : null;
            }

            incoming.CopyTo(target);
            if (!resident)
            {
                _chunks[coord] = new ImageChunk(chunkPixels) { Dirty = true };
                _removedSincePersist.Remove(coord);
                _strokeChanged?.Add(coord);
                touched[coord] = true;
            }
            else
            {
                touched[coord] = false;
            }
        });

        foreach ((ImageChunkCoord coord, bool created) in touched)
        {
            if (!created)
            {
                ImageChunk chunk = _chunks[coord];
                CommitPaintedChunk(coord, chunk.Pixels, resident: true, chunk, mayZero: true);
            }
        }

        if (touched.Count == 0)
        {
            return false;
        }

        MarkDirtyPixels(x, y, x + width - 1, y + height - 1);
        BumpContent();
        return true;
    }

    private bool RegionInside(int x, int y, int width, int height) =>
        width > 0 && height > 0 && x >= 0 && y >= 0 && x + width <= _width && y + height <= _height;

    // Splits a pixel rectangle into per-row runs that each stay inside one chunk. The callback gets the
    // chunk, the run's pixel offset within it, its pixel offset within the rectangle, and its length.
    private void ForEachRun(int x, int y, int width, int height, Action<ImageChunkCoord, int, int, int> visit)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width;)
            {
                var coord = new ImageChunkCoord(px / _chunkSize, py / _chunkSize);
                int run = Math.Min(((coord.X + 1) * _chunkSize) - px, x + width - px);
                int chunkOffset = ((py - (coord.Y * _chunkSize)) * _chunkSize) + (px - (coord.X * _chunkSize));
                int regionOffset = ((py - y) * width) + (px - x);
                visit(coord, chunkOffset, regionOffset, run);
                px += run;
            }
        }
    }

    /// <summary>Converts a UV range on this image into the inclusive, clamped chunk-coordinate rect
    /// that covers it, grown by <paramref name="headroomChunks"/> on every side — what a caller that
    /// needs "this footprint plus some slack" (residency's target computation) asks for, distinct from
    /// the exact rect <see cref="Paint"/> derives for itself with no headroom.</summary>
    public ImageChunkRect ChunkRectForUv(float uMin, float uMax, float vMin, float vMax, int headroomChunks = 0)
    {
        int pxMin = Math.Clamp(Mathf.FloorToInt(Mathf.Min(uMin, uMax) * _width), 0, _width - 1);
        int pxMax = Math.Clamp(Mathf.CeilToInt(Mathf.Max(uMin, uMax) * _width), 0, _width - 1);
        int pyMin = Math.Clamp(Mathf.FloorToInt(Mathf.Min(vMin, vMax) * _height), 0, _height - 1);
        int pyMax = Math.Clamp(Mathf.CeilToInt(Mathf.Max(vMin, vMax) * _height), 0, _height - 1);

        int cxMin = Math.Clamp((pxMin / _chunkSize) - headroomChunks, 0, _chunksX - 1);
        int cxMax = Math.Clamp((pxMax / _chunkSize) + headroomChunks, 0, _chunksX - 1);
        int cyMin = Math.Clamp((pyMin / _chunkSize) - headroomChunks, 0, _chunksY - 1);
        int cyMax = Math.Clamp((pyMax / _chunkSize) + headroomChunks, 0, _chunksY - 1);

        return new ImageChunkRect(cxMin, cyMin, cxMax, cyMax);
    }

    /// <summary>Snapshots the current chunk set for a landscape build to sample from — typically off
    /// the main thread while this image may keep being painted on it. See <see cref="ImageChunkTable"/>
    /// for why a snapshot rather than a live view.</summary>
    public ImageSampler CreateSampler() => new(new ImageChunkTable(new Dictionary<ImageChunkCoord, ImageChunk>(_chunks)), _width, _height, _chunkSize, _components, _format);

    /// <summary>A sampler over a caller-supplied chunk table rather than the resident set — what an
    /// offline build hands in, having loaded the chunks it needs itself rather than through residency.
    /// Null falls back to a snapshot of the resident set.</summary>
    public ImageSampler CreateSampler(ImageChunkTable? chunks) =>
        chunks is { } table
            ? new ImageSampler(table, _width, _height, _chunkSize, _components, _format)
            : CreateSampler();

    private void RecomputeGrid()
    {
        _chunksX = (_width + _chunkSize - 1) / _chunkSize;
        _chunksY = (_height + _chunkSize - 1) / _chunkSize;
    }

    // Every chunk this produces is dirty: it is called from ReplacePixels/Resize/LoadPixels, which are
    // always edits (or an undo apply/revert, itself just an edit landing at a different value) — never
    // a load from storage, which goes through LoadChunks/LoadManifest instead and marks clean.
    private void RebuildChunks(byte[] dense)
    {
        var chunks = new Dictionary<ImageChunkCoord, ImageChunk>();
        for (int cy = 0; cy < _chunksY; cy++)
        {
            for (int cx = 0; cx < _chunksX; cx++)
            {
                var coord = new ImageChunkCoord(cx, cy);
                if (ExtractChunk(dense, cx, cy) is { } pixels)
                {
                    chunks[coord] = new ImageChunk(pixels) { Dirty = true };
                    _removedSincePersist.Remove(coord);
                }
            }
        }

        // A chunk resident before the rebuild but not after — including one from a since-shrunk grid,
        // or one the new content simply no longer touches — is explicitly gone, not just re-chunked.
        foreach (ImageChunkCoord coord in _chunks.Keys)
        {
            if (!chunks.ContainsKey(coord))
            {
                _removedSincePersist.Add(coord);
            }
        }

        _chunks = chunks;
    }

    /// <summary>Gathers one chunk's worth of pixels out of a dense buffer, or null if every pixel in
    /// its (canvas-clipped) footprint is zero — the source of the "absent chunk means all-zero"
    /// invariant every other member relies on. Each pixel is <see cref="Components"/> interleaved
    /// bytes, copied as one run rather than per-component.</summary>
    private byte[]? ExtractChunk(byte[] dense, int cx, int cy)
    {
        int stride = Stride;
        var pixels = new byte[_chunkSize * _chunkSize * stride];
        int baseX = cx * _chunkSize;
        int baseY = cy * _chunkSize;
        int width = Math.Min(_chunkSize, _width - baseX);
        int height = Math.Min(_chunkSize, _height - baseY);
        bool any = false;

        for (int y = 0; y < height; y++)
        {
            int denseRow = (((baseY + y) * _width) + baseX) * stride;
            int localRow = y * _chunkSize * stride;
            int rowBytes = width * stride;
            Array.Copy(dense, denseRow, pixels, localRow, rowBytes);

            for (int i = 0; i < rowBytes; i++)
            {
                if (pixels[localRow + i] != 0)
                {
                    any = true;
                    break;
                }
            }
        }

        return any ? pixels : null;
    }

    private void CopyChunkInto(byte[] dense, ImageChunkCoord coord, byte[] pixels)
    {
        int stride = Stride;
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
            int denseRow = (((baseY + y) * _width) + baseX) * stride;
            int localRow = y * _chunkSize * stride;
            Array.Copy(pixels, localRow, dense, denseRow, width * stride);
        }
    }

    /// <summary>Paints the part of one brush stamp that falls in one chunk. A chunk that is stored but
    /// not currently resident refuses the whole stamp: fabricating a zero buffer over it would silently
    /// destroy real pixels at the next commit, and there is no way to know what erasing it should even
    /// do until it actually loads. A truly empty chunk (never painted, never stored) still materializes
    /// on first non-erase write and stays absent for a no-op erase, exactly as before chunking
    /// existed.</summary>
    private bool PaintChunk(ImageChunkCoord coord, int minX, int maxX, int minY, int maxY, float u, float v, float radiusU, float radiusV, byte amount, bool erase, float hardness)
    {
        int components = _components;
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

        bool resident = _chunks.TryGetValue(coord, out ImageChunk? existing);
        if (!resident && IsStored(coord))
        {
            return false;
        }

        if (!resident && erase)
        {
            // Erasing a chunk that exists nowhere — not resident, not stored — changes nothing.
            return false;
        }

        // Recorded once per stroke, on this chunk's first touch — a later dab in the same stroke must
        // not overwrite it with an already-painted-on state, or undo would only revert the last dab.
        if (_strokeBefore is { } stroke && !stroke.ContainsKey(coord))
        {
            stroke[coord] = resident ? (byte[])existing!.Pixels.Clone() : null;
        }

        byte[] pixels = resident ? existing!.Pixels : new byte[_chunkSize * _chunkSize * components];
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

                float weight = BrushFalloff.Weight(distance, hardness);
                int delta = Mathf.RoundToInt(amount * weight);
                if (delta == 0)
                {
                    continue;
                }

                int localX = px - chunkBaseX;
                int localY = py - chunkBaseY;
                int index = (((localY * _chunkSize) + localX) * components) + 0;
                byte before = pixels[index];
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

        if (changed)
        {
            CommitPaintedChunk(coord, pixels, resident, existing, mayZero: erase);
        }

        return changed;
    }

    /// <summary>The <see cref="PaintImagePixelFormat.Float32"/> counterpart to <see cref="PaintChunk"/> —
    /// same brush maths, but <paramref name="amount"/> and every pixel value are raw floats read and
    /// written through <see cref="PaintImagePixelIO"/> instead of a byte saturating at [0,255]. Only
    /// ever called on a scalar image — see <see cref="ConfigureNew"/> for why Float32 never coexists
    /// with a 3/4-component image.</summary>
    private bool PaintChunkFloat(ImageChunkCoord coord, int minX, int maxX, int minY, int maxY, float u, float v, float radiusU, float radiusV, float amount, bool erase, float hardness)
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

        bool resident = _chunks.TryGetValue(coord, out ImageChunk? existing);
        if (!resident && IsStored(coord))
        {
            return false;
        }

        if (!resident && erase)
        {
            return false;
        }

        if (_strokeBefore is { } stroke && !stroke.ContainsKey(coord))
        {
            stroke[coord] = resident ? (byte[])existing!.Pixels.Clone() : null;
        }

        byte[] pixels = resident ? existing!.Pixels : new byte[_chunkSize * _chunkSize * Stride];

        // Scalar Float32 image, one component per pixel, so the chunk is just a float grid — cast it
        // once here rather than re-deriving a byte offset and rebuilding a span per pixel.
        Span<float> values = MemoryMarshal.Cast<byte, float>(pixels);

        // dx² per column, computed once for the whole stamp rather than once per row: the falloff is
        // radially symmetric, so each row only adds its own dy².
        float[] columnDistSq = _stampColumnDistSq ??= new float[_chunkSize];
        float invWidth = 1.0f / _width;
        float invHeight = 1.0f / _height;
        float invRadiusU = 1.0f / radiusU;
        float invRadiusV = 1.0f / radiusV;
        for (int px = loX; px <= hiX; px++)
        {
            float dx = (((px + 0.5f) * invWidth) - u) * invRadiusU;
            columnDistSq[px - loX] = dx * dx;
        }

        // erase subtracts the same non-negative weight the brush would otherwise add; sign is exactly
        // ±1, so folding it into amount here is exact.
        float amountSign = erase ? -amount : amount;
        int count = hiX - loX + 1;
        ReadOnlySpan<float> rowColumnDistSq = columnDistSq.AsSpan(0, count);
        bool changed = false;

        for (int py = loY; py <= hiY; py++)
        {
            float dy = (((py + 0.5f) * invHeight) - v) * invRadiusV;
            int rowStart = ((py - chunkBaseY) * _chunkSize) - chunkBaseX + loX;
            BrushFalloff.AddRow(values.Slice(rowStart, count), rowColumnDistSq, dy * dy, amountSign, hardness, ref changed);
        }

        if (changed)
        {
            CommitPaintedChunk(coord, pixels, resident, existing, mayZero: erase);
        }

        return changed;
    }

    /// <summary>The color-aware counterpart to <see cref="PaintChunk"/> — see the type doc on the
    /// public <see cref="Paint(float,float,float,float,Color,float,bool,float)"/> overload for the blend
    /// rules. Only ever called for a 3- or 4-component image.</summary>
    private bool PaintChunkColor(
        ImageChunkCoord coord, int minX, int maxX, int minY, int maxY,
        float u, float v, float radiusU, float radiusV, Color color, byte amount, bool erase, float hardness)
    {
        int components = _components;
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

        bool resident = _chunks.TryGetValue(coord, out ImageChunk? existing);
        if (!resident && IsStored(coord))
        {
            return false;
        }

        if (!resident && erase)
        {
            return false;
        }

        if (_strokeBefore is { } stroke && !stroke.ContainsKey(coord))
        {
            stroke[coord] = resident ? (byte[])existing!.Pixels.Clone() : null;
        }

        byte[] pixels = resident ? existing!.Pixels : new byte[_chunkSize * _chunkSize * components];
        bool changed = false;
        byte targetR = ToByte(color.R);
        byte targetG = ToByte(color.G);
        byte targetB = ToByte(color.B);

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

                float weight = BrushFalloff.Weight(distance, hardness);
                int delta = Mathf.RoundToInt(amount * weight);
                if (delta == 0)
                {
                    continue;
                }

                int localX = px - chunkBaseX;
                int localY = py - chunkBaseY;
                int index = (((localY * _chunkSize) + localX) * components) + 0;

                if (components == 4)
                {
                    int alphaIndex = index + 3;
                    byte beforeAlpha = pixels[alphaIndex];
                    byte afterAlpha = erase
                        ? (byte)Math.Max(0, beforeAlpha - delta)
                        : (byte)Math.Min(255, beforeAlpha + delta);

                    if (afterAlpha == beforeAlpha)
                    {
                        continue;
                    }

                    pixels[alphaIndex] = afterAlpha;
                    changed = true;

                    if (afterAlpha == 0)
                    {
                        // Keeps "a fully erased texel is all-zero" true through an erase, the same
                        // invariant IsAllZero/CommitPaintedChunk relies on to prune the chunk back to
                        // absent — RGB left behind at zero alpha would otherwise never get cleared.
                        pixels[index] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                    }
                    else if (!erase)
                    {
                        float applied = Math.Abs(afterAlpha - beforeAlpha) / 255.0f;
                        pixels[index] = LerpByte(pixels[index], targetR, applied);
                        pixels[index + 1] = LerpByte(pixels[index + 1], targetG, applied);
                        pixels[index + 2] = LerpByte(pixels[index + 2], targetB, applied);
                    }
                }
                else
                {
                    // No alpha channel to weigh by: the brush's own falloff weight is the lerp factor
                    // directly, toward the brush color when painting or toward black when erasing.
                    float t = delta / 255.0f;
                    byte beforeR = pixels[index];
                    byte beforeG = pixels[index + 1];
                    byte beforeB = pixels[index + 2];
                    byte afterR = LerpByte(beforeR, erase ? (byte)0 : targetR, t);
                    byte afterG = LerpByte(beforeG, erase ? (byte)0 : targetG, t);
                    byte afterB = LerpByte(beforeB, erase ? (byte)0 : targetB, t);

                    if (afterR != beforeR || afterG != beforeG || afterB != beforeB)
                    {
                        pixels[index] = afterR;
                        pixels[index + 1] = afterG;
                        pixels[index + 2] = afterB;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            // A 3-component (no-alpha) image lerps RGB straight toward the brush colour, so even a
            // non-erase stamp toward black can zero a pixel; every other case only zeroes on erase.
            CommitPaintedChunk(coord, pixels, resident, existing, mayZero: erase || _components == 3);
        }

        return changed;
    }

    // Shared by PaintChunk and PaintChunkColor: folds a changed chunk's pixels back into residency,
    // pruning it back to absent if the edit brought it all the way to all-zero — the moment that keeps
    // "an absent chunk is all-zero" true immediately rather than eventually at the next commit.
    private void CommitPaintedChunk(ImageChunkCoord coord, byte[] pixels, bool resident, ImageChunk? existing, bool mayZero)
    {
        _strokeChanged?.Add(coord);

        if (resident)
        {
            // A stamp that only ever raised values cannot have brought a resident (already non-zero)
            // chunk to all-zero, so the 256 KB scan only runs when the stamp could have lowered one.
            if (mayZero && IsAllZero(pixels))
            {
                _chunks.Remove(coord);
                _removedSincePersist.Add(coord);
            }
            else
            {
                existing!.Dirty = true;
                existing.Revision++;
            }
        }
        else
        {
            _chunks[coord] = new ImageChunk(pixels) { Dirty = true };
            _removedSincePersist.Remove(coord);
        }
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(Mathf.RoundToInt(channel * 255.0f), 0, 255);

    private static byte LerpByte(byte from, byte to, float t) =>
        (byte)Math.Clamp(Mathf.RoundToInt(from + ((to - from) * t)), 0, 255);
}
