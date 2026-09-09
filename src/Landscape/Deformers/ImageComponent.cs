using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Places a bound <see cref="PaintImage"/> in the scene as a landscape paint mask. The component owns no
/// raster data itself — only which image it references, plus placement (world size, strength, which
/// channel it feeds) — so many placements can share one image and painting it from any of them
/// updates every placement. See <see cref="ProceduralComponent"/> for the same split applied to
/// procedural meshes.
/// </summary>
public sealed class ImageComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer, IIncrementalLandscapeDeformer, IPreparableLandscapeDeformer, ISceneNodeComponent, IMeshPickable
{
    /// <summary>The single source of truth for this component kind's id — <see cref="ImageComponentType"/>
    /// and <see cref="ImageComponentPersistence"/> both reference this instead of restating it.</summary>
    public const string Kind = "image";

    private const float BoundsHeight = 2.0f;

    private static Shader? _objectBackdropShader;

    private readonly ImageSystem _system;
    private int? _imageId;
    private int? _displayLayerId;
    private float _worldSizeX = 64.0f;
    private float _worldSizeZ = 64.0f;

    // What the viewport representation was last built from. Compared by ImageSystem.Update against
    // the live pair every frame, mirroring ProceduralComponent's _representedModelId/_representedRevision —
    // see that class for why this lives here rather than being recomputed from scratch each frame.
    private int? _representedImageId;
    private int _representedImageRevision = -1;
    private int? _representedDisplayLayerId;
    private int _representedDisplayLayerRevision = -1;

    // The node BuildNode last produced, and the per-chunk children hanging off it keyed by which chunk
    // and which revision of it they show. Held so a content change can re-upload just the chunks that
    // actually moved (SyncChunkNodes) instead of tearing the whole representation down and rebuilding
    // every chunk's texture — the difference between a paint stroke costing one texture upload a frame
    // and costing one per chunk in the entire image, every frame.
    private Node3D? _root;
    private readonly Dictionary<ImageChunkCoord, ChunkNode> _chunkNodes = [];

    // The chunk table an offline build loaded for this placement, or null on the live path, where
    // residency has already put the pixels on the shared image. Set once by Prepare, read by Rasterize.
    private ImageChunkTable? _preparedChunks;

    // What ConsumeDirtyRegions last saw, kept separate from _chunkNodes above: that one only exists
    // while a display layer is bound to something other than None, but the landscape rebuild this
    // drives needs to work for a placement with no viewport representation at all. The sequence is
    // this placement's own cursor into the bound image's shared dirty log, so several placements of
    // one image each consume the same edits independently.
    private int? _dirtyTrackedImageId;
    private float _dirtyTrackedStrength;
    private string _dirtyTrackedChannel = "";
    private long _dirtyTrackedSeq = -1;

    // Object display mode's backdrop cuts a hole for every chunk quad resident over it (see
    // BuildObjectMesh) instead of racing that quad for the same depth — this is where the "which
    // cells are covered" data for that cutout lives. One texel per chunk-grid cell, not per canvas
    // pixel, so it stays tiny regardless of how large the bound image is. Null whenever the
    // representation isn't Object mode (BuildNode never creates it for LandscapeOverlay).
    private Image? _objectMaskImage;
    private ImageTexture? _objectMaskTexture;

    private sealed record ChunkNode(Node3D Node, ImageTexture Texture)
    {
        public int Revision { get; set; } = -1;

        // The widened pixels and the Godot Image handed to ImageTexture.Update, kept per chunk rather
        // than rebuilt per upload. A stroke re-uploads every chunk under the brush every frame, and a
        // full tile is a quarter of a megabyte, so allocating both each time buried the stamp itself.
        public byte[]? Pixels { get; set; }

        public Image? Buffer { get; set; }
    }

    public ImageComponent(ImageSystem system)
    {
        // Guarded for the same reason ProceduralComponent guards its system: a null here is otherwise
        // invisible until first rasterized, then surfaces as a NullReferenceException deep in a
        // landscape build with no hint the real mistake was made back at construction time.
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

    [ScriptProperty]
    public int? ImageId
    {
        get => _imageId;
        set
        {
            if (_imageId == value)
            {
                return;
            }

            _imageId = value;
            Owner?.RefreshRepresentation();
        }
    }

    /// <summary>The bound image, or null if <see cref="ImageId"/> is unset or dangling.</summary>
    public PaintImage? Image => _system.FindImage(_imageId);

    /// <summary>Bound image pixel width, or null when nothing is bound. Read-only diagnostic accessor
    /// so a script can size a brush or a rebuild-wave check against the actual raster.</summary>
    [ScriptProperty]
    public int? ImageWidth => Image?.Width;

    /// <summary>Bound image pixel height, or null when nothing is bound.</summary>
    [ScriptProperty]
    public int? ImageHeight => Image?.Height;

    /// <summary>Bound image chunk tile size, or null when nothing is bound.</summary>
    [ScriptProperty]
    public int? ImageChunkSize => Image?.ChunkSize;

    /// <summary>Bound image stored pixel format (<c>Byte</c> or <c>Float32</c>), or null when nothing
    /// is bound.</summary>
    [ScriptProperty]
    public string? ImagePixelFormat => Image?.Format.ToString();

    public int? DisplayLayerId
    {
        get => _displayLayerId;
        set
        {
            if (_displayLayerId == value)
            {
                return;
            }

            _displayLayerId = value;
            Owner?.RefreshRepresentation();
        }
    }

    /// <summary>The bound display layer, or null if <see cref="DisplayLayerId"/> is unset or dangling.
    /// Purely a viewport concern — see <see cref="BuildNode"/> — never consulted by <see cref="Rasterize"/>
    /// or <see cref="ContentVersion"/>.</summary>
    public ImageDisplayLayer? DisplayLayer => _system.FindDisplayLayer(_displayLayerId);

    [ScriptProperty]
    public float WorldSizeX
    {
        get => _worldSizeX;
        set
        {
            float clamped = Mathf.Max(0.5f, value);
            if (_worldSizeX == clamped)
            {
                return;
            }

            _worldSizeX = clamped;
            Owner?.RefreshRepresentation();
        }
    }

    [ScriptProperty]
    public float WorldSizeZ
    {
        get => _worldSizeZ;
        set
        {
            float clamped = Mathf.Max(0.5f, value);
            if (_worldSizeZ == clamped)
            {
                return;
            }

            _worldSizeZ = clamped;
            Owner?.RefreshRepresentation();
        }
    }

    [ScriptProperty]
    public float Strength { get; set; } = 1.0f;

    [ScriptProperty]
    public string Channel { get; set; } = "";

    public override string TypeId => Kind;

    public override string DisplayName => "Image";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public SelfScale SelfScale => SelfScale.None;

    public Aabb LocalBounds => new(
        new Vector3(-WorldSizeX * 0.5f, -BoundsHeight * 0.5f, -WorldSizeZ * 0.5f),
        new Vector3(WorldSizeX, BoundsHeight, WorldSizeZ));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:image"
        : $"entity:new:{Entity.Id.Value}:image";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion => HashCode.Combine(ImageId, Image?.ContentRevision ?? 0, WorldSizeX, WorldSizeZ, Strength, Channel);

    /// <summary>Every loaded placement referencing the same image — what a paint or resize undo
    /// command snapshots chunk fingerprints against, since the edit lands on the shared image, not
    /// this placement alone.</summary>
    public IEnumerable<SceneEntity> AffectedEntities => _imageId is int id
        ? _system.Context.Scene.Entities.Where(entity => entity.Component<ImageComponent>()?.ImageId == id)
        : Owner is { } owner ? [owner] : [];

    /// <summary>An independent placement of the same image — cloning a component shares its bound
    /// image (and display layer) rather than forking the pixels, the same way cloning a
    /// <see cref="ProceduralComponent"/> shares its bound model.</summary>
    public override SceneComponent Clone() => new ImageComponent(_system)
    {
        ImageId = ImageId,
        DisplayLayerId = DisplayLayerId,
        WorldSizeX = WorldSizeX,
        WorldSizeZ = WorldSizeZ,
        Strength = Strength,
        Channel = Channel,
    };

    /// <summary>Whether the bound image or display layer has moved on since this placement's viewport
    /// representation was last built. Covers both kinds of change — see
    /// <see cref="NeedsStructuralRefresh"/> for which of them actually need a full rebuild.</summary>
    public bool NeedsRefresh =>
        NeedsStructuralRefresh || _representedImageRevision != (Image?.ViewRevision ?? -1);

    /// <summary>Whether what changed is something <see cref="SyncChunkNodes"/> cannot patch in place —
    /// a different image, a different display layer, or an edit to the bound layer itself (which can
    /// change the display mode, and so the entire node shape, or the colour ramp every chunk's texture
    /// was baked with). A change to the image's <em>pixels</em> is deliberately not here: that is the
    /// common case, happens every frame of a paint stroke, and only ever needs the touched chunks
    /// re-uploaded.</summary>
    public bool NeedsStructuralRefresh =>
        _representedImageId != ImageId ||
        _representedDisplayLayerId != DisplayLayerId ||
        _representedDisplayLayerRevision != (DisplayLayer?.Revision ?? -1);

    /// <summary>
    /// Builds this placement's viewport preview per its bound <see cref="ImageDisplayLayer"/>'s
    /// <see cref="ImageDisplayMode"/> — nothing for <see cref="ImageDisplayMode.None"/> or while
    /// unbound, a colored <see cref="Decal"/> for <see cref="ImageDisplayMode.LandscapeOverlay"/>, or a
    /// paintable textured quad for <see cref="ImageDisplayMode.Object"/>. Purely a viewport concern —
    /// the landscape channel this placement paints (see <see cref="Rasterize"/>) is unaffected by
    /// display mode either way.
    /// </summary>
    public Node3D? BuildNode()
    {
        _representedImageId = ImageId;
        _representedImageRevision = Image?.ViewRevision ?? -1;
        _representedDisplayLayerId = DisplayLayerId;
        _representedDisplayLayerRevision = DisplayLayer?.Revision ?? -1;
        _chunkNodes.Clear();
        _root = null;
        _objectMaskImage = null;
        _objectMaskTexture = null;

        if (DisplayLayer is not { } layer || Image is not { } image || layer.DisplayMode == ImageDisplayMode.None)
        {
            return null;
        }

        _root = layer.DisplayMode switch
        {
            ImageDisplayMode.LandscapeOverlay => BuildOverlayDecal(image, layer),
            ImageDisplayMode.Object => BuildObjectMesh(image),
            _ => null,
        };

        return _root;
    }

    /// <summary>
    /// Brings the existing representation up to date with the bound image's current chunks, touching
    /// only what changed: a chunk whose <see cref="ImageChunk.Revision"/> moved gets its pixels
    /// re-uploaded into the texture it already has, a newly painted or streamed-in chunk gets a node,
    /// and one that was erased or evicted loses its node. Everything else is left alone.
    ///
    /// This is what makes painting cheap. Rebuilding the representation instead — which is what a
    /// <see cref="SceneEntity.RefreshRepresentation"/> does — allocates a fresh texture for every chunk
    /// in the image, and a paint stroke bumps the image's revision on every frame it drags, so the cost
    /// of one stroke scales with the size of the whole image rather than with the size of the brush.
    ///
    /// Only valid when <see cref="NeedsStructuralRefresh"/> is false; the caller checks that first.
    /// </summary>
    public void SyncChunkNodes()
    {
        // Recorded before the early-out below, not after the work: a placement with nothing to draw
        // (no bound image, display mode None) is still fully in step with what it is bound to, and
        // leaving it looking stale would keep NeedsRefresh true forever.
        _representedImageRevision = Image?.ViewRevision ?? -1;

        if (_root is not { } root || !GodotObject.IsInstanceValid(root) ||
            Image is not { } image || DisplayLayer is not { } layer || layer.DisplayMode == ImageDisplayMode.None)
        {
            return;
        }

        foreach (ImageChunkCoord coord in image.ChunkCoords)
        {
            int revision = image.ChunkRevision(coord);
            if (_chunkNodes.TryGetValue(coord, out ChunkNode? existing))
            {
                if (existing.Revision != revision)
                {
                    UploadChunkImage(image, layer, coord, existing);
                    existing.Revision = revision;
                }

                continue;
            }

            if (AddChunkNode(root, image, layer, coord) is { } added)
            {
                added.Revision = revision;
            }
        }

        // Erased (all-zero) or evicted since the last sync — the coordinate is no longer resident, so
        // whatever it was showing is stale.
        List<ImageChunkCoord>? gone = null;
        foreach ((ImageChunkCoord coord, ChunkNode node) in _chunkNodes)
        {
            if (!image.IsResident(coord))
            {
                (gone ??= []).Add(coord);
                RemoveChunkNode(root, node);
            }
        }

        foreach (ImageChunkCoord coord in gone ?? [])
        {
            _chunkNodes.Remove(coord);
            SetMaskResident(coord, false);
        }
    }

    private static void RemoveChunkNode(Node3D root, ChunkNode node)
    {
        if (!GodotObject.IsInstanceValid(node.Node))
        {
            return;
        }

        // Detached before freeing rather than freed in place: QueueFree is deferred, so a node left as
        // a child would still be holding its name when a later sync re-adds the same coordinate.
        root.RemoveChild(node.Node);
        node.Node.QueueFree();
    }

    /// <summary>Re-uploads one chunk's pixels into the texture it already has, through that chunk
    /// node's own pixel buffer and <see cref="Image"/> rather than a freshly built pair. Both are the
    /// full tile size and a stroke comes back here for every chunk under the brush every frame, so
    /// rebuilding them is what a wide brush actually spends its time on.</summary>
    private static void UploadChunkImage(PaintImage image, ImageDisplayLayer layer, ImageChunkCoord coord, ChunkNode node)
    {
        int width;
        int height;
        node.Pixels = layer.DisplayMode == ImageDisplayMode.LandscapeOverlay && layer.ColorSource == ImageColorSource.Ramp
            ? PaintImageTextures.WriteChunkTintedRgba(image, coord, layer.BaseColor, layer.FullColor, node.Pixels, out width, out height)
            : PaintImageTextures.WriteChunkRgba(image, coord, node.Pixels, out width, out height);

        node.Buffer ??= Godot.Image.CreateEmpty(width, height, false, Godot.Image.Format.Rgba8);
        node.Buffer.SetData(width, height, false, Godot.Image.Format.Rgba8, node.Pixels);
        node.Texture.Update(node.Buffer);
    }

    private ChunkNode? AddChunkNode(Node3D root, PaintImage image, ImageDisplayLayer layer, ImageChunkCoord coord)
    {
        ChunkNode? built = layer.DisplayMode == ImageDisplayMode.LandscapeOverlay
            ? BuildOverlayChunk(image, layer, coord)
            : BuildObjectChunk(image, coord);

        if (built == null)
        {
            return null;
        }

        root.AddChild(built.Node);
        _chunkNodes[coord] = built;
        SetMaskResident(coord, true);
        return built;
    }

    /// <summary>Marks one chunk-grid cell resident (covered by a chunk quad, so the backdrop must cut
    /// a hole there) or not, in the mask <see cref="BuildObjectMesh"/>'s backdrop reads to decide where
    /// to draw itself. A no-op outside Object display mode, where <see cref="_objectMaskImage"/> is
    /// never created.</summary>
    private void SetMaskResident(ImageChunkCoord coord, bool resident)
    {
        if (_objectMaskImage is not { } mask || _objectMaskTexture is not { } texture)
        {
            return;
        }

        mask.SetPixel(coord.X, coord.Y, resident ? Colors.White : Colors.Black);
        texture.Update(mask);
    }

    /// <summary>One decal per resident chunk, each ramping from <see cref="ImageDisplayLayer.BaseColor"/>
    /// to <see cref="ImageDisplayLayer.FullColor"/> as that chunk's pixel values rise, projected
    /// straight down onto the terrain. Per-chunk rather than one decal (and one texture) for the whole
    /// canvas — see <see cref="PaintImageTextures"/> — so this stays cheap regardless of how large the
    /// canvas is; an unpainted image simply gets no decals, the same as a fully transparent one. The
    /// vertical extent mirrors <see cref="LandscapeGrid.NominalHeightExtent"/>, the same "tall enough
    /// regardless of exact placement height" bound <see cref="ProceduralComponent"/> uses for a flat
    /// paint-only placement's box.</summary>
    private Node3D BuildOverlayDecal(PaintImage image, ImageDisplayLayer layer)
    {
        var root = new Node3D { Name = "ImageOverlay" };
        foreach (ImageChunkCoord coord in image.ChunkCoords)
        {
            if (BuildOverlayChunk(image, layer, coord) is { } chunk)
            {
                root.AddChild(chunk.Node);
                _chunkNodes[coord] = chunk;
                chunk.Revision = image.ChunkRevision(coord);
            }
        }

        return root;
    }

    private ChunkNode? BuildOverlayChunk(PaintImage image, ImageDisplayLayer layer, ImageChunkCoord coord)
    {
        if (ChunkPlacementFor(image, coord) is not { } placement)
        {
            return null;
        }

        ImageTexture texture = PaintImageTextures.ChunkTinted(image, coord, layer.BaseColor, layer.FullColor);
        var decal = new Decal
        {
            Name = $"Chunk_{coord.X}_{coord.Y}",
            Position = new Vector3(placement.CenterX, 0.0f, placement.CenterZ),
            Size = new Vector3(placement.SizeX, LandscapeGrid.NominalHeightExtent * 2.0f, placement.SizeZ),
            TextureAlbedo = texture,

            // A decal's default cull mask matches every VisualInstance3D on the default layer, which in
            // this editor is everything — models, markers, the viewport's own reference grid. Restrict
            // it to the layer terrain surfaces render on, so painting only ever visibly colors the
            // ground it is meant to paint.
            CullMask = LandscapeChunk.RenderLayer,
        };

        return new ChunkNode(decal, texture);
    }

    /// <summary>A flat, unshaded backdrop sized to the whole footprint — solid black, no texture, so
    /// its cost does not scale with canvas size — with one further quad layered per resident chunk
    /// showing that chunk's own grayscale texture. The backdrop is what the Paint tool targets directly
    /// (via <see cref="SceneEntity.TryPickGeometry"/>) instead of projecting through the terrain when
    /// this placement's display mode is <see cref="ImageDisplayMode.Object"/>: it exists even before
    /// anything has been painted, so there is always something to click and start painting on.
    ///
    /// Every resident chunk quad sits exactly coplanar with this backdrop, so rather than have the two
    /// race for the same depth (see <see cref="ObjectBackdropShaderCode"/> for what that looked like —
    /// a world-space gap that flickered at distance, then a disabled depth test that drew over models
    /// actually in front of it, then a clip-space depth bias that wasn't a coplanar tie-break at all so
    /// much as an ever-so-slightly-different depth, which is still exactly what z-fighting is), the
    /// backdrop's own shader cuts a hole for every resident coordinate: there is only ever one surface
    /// drawn at a given point on the canvas, so there is nothing left to fight.</summary>
    private Node3D BuildObjectMesh(PaintImage image)
    {
        var root = new Node3D { Name = "ImageObject" };

        // Fully qualified: within this class, the bare name "Image" resolves to the Image property
        // (this placement's bound PaintImage) rather than Godot's Image type — see the class doc comment.
        _objectMaskImage = Godot.Image.CreateEmpty(image.ChunksX, image.ChunksY, false, Godot.Image.Format.R8);
        _objectMaskTexture = ImageTexture.CreateFromImage(_objectMaskImage);
        var backdropMaterial = new ShaderMaterial { Shader = ObjectBackdropShader() };
        backdropMaterial.SetShaderParameter("resident_mask", _objectMaskTexture);
        backdropMaterial.SetShaderParameter("world_size", new Vector2(WorldSizeX, WorldSizeZ));

        root.AddChild(new MeshInstance3D
        {
            Name = "Backdrop",
            Mesh = new PlaneMesh { Size = new Vector2(WorldSizeX, WorldSizeZ) },
            MaterialOverride = backdropMaterial,
        });

        foreach (ImageChunkCoord coord in image.ChunkCoords)
        {
            if (BuildObjectChunk(image, coord) is { } chunk)
            {
                root.AddChild(chunk.Node);
                _chunkNodes[coord] = chunk;
                chunk.Revision = image.ChunkRevision(coord);
                SetMaskResident(coord, true);
            }
        }

        return root;
    }

    private ChunkNode? BuildObjectChunk(PaintImage image, ImageChunkCoord coord)
    {
        if (ChunkPlacementFor(image, coord) is not { } placement)
        {
            return null;
        }

        ImageTexture texture = PaintImageTextures.ChunkTexture(image, coord);
        var mesh = new MeshInstance3D
        {
            Name = $"Chunk_{coord.X}_{coord.Y}",
            // Exactly coplanar with the backdrop, and safely so — the backdrop's own shader knows to
            // leave a hole here (see BuildObjectMesh), so this is the only thing ever drawn at this
            // point on the canvas, tested and written against the depth buffer completely normally.
            Position = new Vector3(placement.CenterX, 0.0f, placement.CenterZ),
            Mesh = new PlaneMesh { Size = new Vector2(placement.SizeX, placement.SizeZ) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,

                // Clamp, not the default repeat. Each chunk is its own texture covering its own quad,
                // so a bilinear tap at a quad's edge would otherwise wrap around and blend in pixels
                // from the *opposite* edge of the same chunk — drawing a bright seam along every chunk
                // boundary wherever the far side happened to be painted.
                TextureRepeat = false,
            },
        };

        return new ChunkNode(mesh, texture);
    }

    private static Shader ObjectBackdropShader() => _objectBackdropShader ??= new Shader { Code = ObjectBackdropShaderCode };

    // One texel per chunk-grid cell (not per canvas pixel — see _objectMaskImage), sampled at a UV
    // computed the same way TryLocalToUv computes one, rather than trusting PlaneMesh's own implicit
    // UV to happen to agree with it: a mismatched V direction there would silently cut holes in the
    // wrong place instead of the right one, which is a much worse failure mode to chase than this
    // extra varying is worth avoiding.
    private const string ObjectBackdropShaderCode = """
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D resident_mask : hint_default_black, filter_nearest, repeat_disable;
uniform vec2 world_size = vec2(1.0, 1.0);

varying vec2 canvas_uv;

void vertex() {
    canvas_uv = (VERTEX.xz / world_size) + vec2(0.5);
}

void fragment() {
    if (texture(resident_mask, canvas_uv).r > 0.5) {
        discard;
    }

    ALBEDO = vec3(0.0);
}
""";

    private readonly record struct ChunkPlacement(float CenterX, float CenterZ, float SizeX, float SizeZ);

    /// <summary>Where one chunk's textured quad/decal goes in this placement's local space — the
    /// inverse of <see cref="TryLocalToUv"/>, narrowed to just that chunk's pixel sub-rect. Null if the
    /// chunk falls outside the canvas (only possible for a stale coordinate from a since-shrunk image).</summary>
    private ChunkPlacement? ChunkPlacementFor(PaintImage image, ImageChunkCoord coord)
    {
        int baseX = coord.X * image.ChunkSize;
        int baseY = coord.Y * image.ChunkSize;
        int width = Math.Min(image.ChunkSize, image.Width - baseX);
        int height = Math.Min(image.ChunkSize, image.Height - baseY);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        float u0 = (float)baseX / image.Width;
        float u1 = (float)(baseX + width) / image.Width;
        float v0 = (float)baseY / image.Height;
        float v1 = (float)(baseY + height) / image.Height;

        float loX = (u0 - 0.5f) * WorldSizeX;
        float hiX = (u1 - 0.5f) * WorldSizeX;
        float loZ = (v0 - 0.5f) * WorldSizeZ;
        float hiZ = (v1 - 0.5f) * WorldSizeZ;

        return new ChunkPlacement((loX + hiX) * 0.5f, (loZ + hiZ) * 0.5f, hiX - loX, hiZ - loZ);
    }

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Request(LandscapeBuildRequest request)
    {
        if (Image is { } image && ChunksNeededFor(request.Region) is { } rect)
        {
            request.ImageChunks(image, rect);
        }
    }

    public void Prepare(LandscapeBuildResources resources) =>
        _preparedChunks = Image is { } image ? resources.ImageChunks(image) : ImageChunkTable.Empty;

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (Image is not { } image)
        {
            return;
        }

        LandscapeChannelBinding binding = LandscapeChannelBinding.Parse(Channel);
        if (context.Channel(binding.Channel) is not { } channel || context.Writer(binding.Channel) is not { } writer)
        {
            return;
        }

        // Nothing painted under this chunk: every sample below would read zero from an absent image
        // chunk, and a zero contributes nothing (both write paths skip a non-positive value, and the
        // write is a Max against an already-zeroed buffer), so skipping is exactly equivalent to
        // running the full loop. This is what keeps a big footprint cheap — without it, one small
        // painted spot on a large canvas still costs a full resolution² bilinear sweep on every
        // landscape chunk the footprint happens to cover, almost all of it sampling nothing.
        if (!HasPixelsIn(context.Grid.BoundsOf(context.Coord)))
        {
            return;
        }

        int resolution = channel.Resolution;
        Transform3D inverse = Entity.Transform.AffineInverse();

        // Texel centre -> entity-local -> uv is affine in (x, y), so instead of a chunk-origin
        // recompute, a full Transform3D multiply and a division per texel (TexelCentre / TryLocalToUv),
        // step entity-local space by a constant vector per column and rebuild it once per row.
        Vector3 chunkOrigin = context.Grid.OriginOf(context.Coord);
        float texelStep = context.Grid.ChunkSize / resolution;
        Vector3 columnStep = inverse.Basis * new Vector3(texelStep, 0.0f, 0.0f);
        float invWorldSizeX = 1.0f / WorldSizeX;
        float invWorldSizeZ = 1.0f / WorldSizeZ;

        // Snapshotted once per rasterize rather than sampled straight off the image: this runs on a
        // landscape build worker while the image may be being painted concurrently on the main
        // thread — see ImageChunkTable for why a snapshot is what makes that safe. An offline build
        // hands its own prepared table in; the live path passes null and gets the resident set.
        ImageSampler sampler = image.CreateSampler(_preparedChunks);

        // Only a color destination fed by an explicit-or-native "give me the color" binding writes a
        // color; anything else — a scalar destination, or a binding that names one component — reduces
        // to a single number the same way LandscapeChannelPool.SampleScalar does for a material.
        bool writeColor = writer.Components > 1 &&
            binding.Swizzle is LandscapeSwizzle.Native or LandscapeSwizzle.Rgb or LandscapeSwizzle.Rgba;

        for (int y = 0; y < resolution; y++)
        {
            // Rebuilt per row rather than accumulated across the whole grid, so column-step rounding
            // cannot drift past one row's width.
            Vector3 local = inverse * new Vector3(
                chunkOrigin.X + (0.5f * texelStep),
                0.0f,
                chunkOrigin.Z + ((y + 0.5f) * texelStep));

            for (int x = 0; x < resolution; x++, local += columnStep)
            {
                float u = (local.X * invWorldSizeX) + 0.5f;
                float v = (local.Z * invWorldSizeZ) + 0.5f;
                if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f)
                {
                    continue;
                }

                Color scaled = ScaleForStrength(sampler.SampleColor(u, v), image.Components, Strength);

                if (writeColor)
                {
                    if (scaled.R <= 0.0f && scaled.G <= 0.0f && scaled.B <= 0.0f && scaled.A <= 0.0f)
                    {
                        continue;
                    }

                    Color current = writer.GetColor(x, y);
                    writer.SetColor(x, y, new Color(
                        Mathf.Min(1.0f, Mathf.Max(current.R, scaled.R)),
                        Mathf.Min(1.0f, Mathf.Max(current.G, scaled.G)),
                        Mathf.Min(1.0f, Mathf.Max(current.B, scaled.B)),
                        Mathf.Min(1.0f, Mathf.Max(current.A, scaled.A))));
                }
                else
                {
                    float value = LandscapeChannelBinding.Extract(scaled, binding.Swizzle);
                    if (value <= 0.0f)
                    {
                        continue;
                    }

                    // Unclamped: a scalar channel is not necessarily a [0,1] mask — one feeding
                    // ChannelHeightOffset with Amount left at 1 is a direct world-height buffer, and a
                    // ceiling here would silently flatten a strength-scaled heightmap image to Amount's
                    // own value regardless of how high Strength was pushed.
                    writer.Set(x, y, Mathf.Max(writer.Get(x, y), value));
                }
            }
        }
    }

    /// <summary>
    /// Applies <see cref="Strength"/> to a sampled pixel before it is written: an RGBA source scales
    /// only alpha, since scaling RGB directly would darken the painted color rather than fade its
    /// contribution; a source with no separate alpha (scalar or RGB) has nothing else to scale, so
    /// every component is scaled instead — accepting that an RGB source's color does fade with
    /// strength, which is exactly why a source that needs strength-independent color should be RGBA.
    /// </summary>
    private static Color ScaleForStrength(Color sampled, int sourceComponents, float strength) =>
        sourceComponents == 4
            ? new Color(sampled.R, sampled.G, sampled.B, sampled.A * strength)
            : new Color(sampled.R * strength, sampled.G * strength, sampled.B * strength, sampled.A * strength);

    /// <summary>Stamps a soft circular brush at a local-space point, in the bound image's pixels.
    /// False if unbound or the point falls outside this placement's footprint. Scalar — has no color
    /// to paint with; kept for a caller that never has one.</summary>
    public bool Paint(Vector3 local, float radius, float opacity, bool erase)
    {
        if (Image is not { } image || !TryLocalToUv(local, out float u, out float v))
        {
            return false;
        }

        float radiusX = radius / WorldSizeX;
        float radiusY = radius / WorldSizeZ;
        return image.Paint(u, v, radiusX, radiusY, opacity, erase);
    }

    /// <summary>Color-aware counterpart to the scalar <see cref="Paint(Vector3,float,float,bool)"/> —
    /// see <see cref="PaintImage.Paint(float,float,float,float,Color,float,bool)"/> for the blend
    /// rules. Identical to the scalar overload on a scalar-format image, where <paramref name="color"/>
    /// is unused.</summary>
    public bool Paint(Vector3 local, float radius, Color color, float opacity, bool erase)
    {
        if (Image is not { } image || !TryLocalToUv(local, out float u, out float v))
        {
            return false;
        }

        float radiusX = radius / WorldSizeX;
        float radiusY = radius / WorldSizeZ;
        return image.Paint(u, v, radiusX, radiusY, color, opacity, erase);
    }

    /// <summary>The bound image's chunks this placement needs resident to cover a world-space region
    /// — typically <see cref="StreamingSystem.LoadRegion"/> — or null if unbound, or the region misses
    /// this placement's footprint entirely. What <see cref="ImageResidencySystem"/> unions per image
    /// across every placement referencing it, since several can share one.
    ///
    /// Approximate under rotation: the region's corners are transformed into local space and bounded
    /// there rather than intersected exactly, so a rotated footprint can ask for a few chunks it does
    /// not strictly need — never the reverse, which is what would actually matter (a build sampling a
    /// chunk that was never requested).</summary>
    public ImageChunkRect? ChunksNeededFor(Aabb worldRegion)
    {
        if (Image is not { } image)
        {
            return null;
        }

        Transform3D inverse = Entity.Transform.AffineInverse();
        float minLocalX = float.MaxValue, maxLocalX = float.MinValue;
        float minLocalZ = float.MaxValue, maxLocalZ = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? worldRegion.Position.X : worldRegion.End.X,
                (i & 2) == 0 ? worldRegion.Position.Y : worldRegion.End.Y,
                (i & 4) == 0 ? worldRegion.Position.Z : worldRegion.End.Z);
            Vector3 local = inverse * corner;
            minLocalX = Mathf.Min(minLocalX, local.X);
            maxLocalX = Mathf.Max(maxLocalX, local.X);
            minLocalZ = Mathf.Min(minLocalZ, local.Z);
            maxLocalZ = Mathf.Max(maxLocalZ, local.Z);
        }

        float halfX = WorldSizeX * 0.5f;
        float halfZ = WorldSizeZ * 0.5f;
        float loX = Mathf.Max(minLocalX, -halfX);
        float hiX = Mathf.Min(maxLocalX, halfX);
        float loZ = Mathf.Max(minLocalZ, -halfZ);
        float hiZ = Mathf.Min(maxLocalZ, halfZ);
        if (loX > hiX || loZ > hiZ)
        {
            return null;
        }

        float uMin = (loX / WorldSizeX) + 0.5f;
        float uMax = (hiX / WorldSizeX) + 0.5f;
        float vMin = (loZ / WorldSizeZ) + 0.5f;
        float vMax = (hiZ / WorldSizeZ) + 0.5f;

        return image.ChunkRectForUv(uMin, uMax, vMin, vMax, headroomChunks: 1);
    }

    /// <summary>Whether the bound image has any pixels under a world region — the cheap "is there
    /// anything painted here at all" test <see cref="Rasterize"/> early-outs on. Consults the prepared
    /// table on an offline build, the image's resident set otherwise. Reuses <see cref="ChunksNeededFor"/>,
    /// so it inherits that method's one-chunk headroom: over-inclusive by design, since a bilinear tap
    /// near a chunk's edge reads into its neighbour, and answering "yes" when the region is in fact
    /// empty only costs a sweep that writes nothing.</summary>
    private bool HasPixelsIn(Aabb worldRegion)
    {
        if (Image is not { } image || ChunksNeededFor(worldRegion) is not { } rect)
        {
            return false;
        }

        foreach (ImageChunkCoord coord in rect.Coords())
        {
            bool present = _preparedChunks is { } prepared
                ? prepared.TryGet(coord, out _)
                : image.IsResident(coord);
            if (present)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The world-space AABB covering a set of the bound image's chunk coordinates — the
    /// inverse of the mapping <see cref="ChunksNeededFor"/> uses. What a paint stroke's undo command
    /// narrows <see cref="ChunkChangeSnapshot"/> bounds to, so a small stroke on a huge image marks
    /// only the terrain under it dirty rather than this placement's whole footprint. Null if unbound
    /// or given no coordinates.</summary>
    public Aabb? WorldBoundsForChunks(IReadOnlyCollection<ImageChunkCoord> coords)
    {
        if (Image is not { } image || coords.Count == 0)
        {
            return null;
        }

        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;

        foreach (ImageChunkCoord coord in coords)
        {
            int baseX = coord.X * image.ChunkSize;
            int baseY = coord.Y * image.ChunkSize;
            minU = Mathf.Min(minU, (float)baseX / image.Width);
            maxU = Mathf.Max(maxU, (float)Mathf.Min(baseX + image.ChunkSize, image.Width) / image.Width);
            minV = Mathf.Min(minV, (float)baseY / image.Height);
            maxV = Mathf.Max(maxV, (float)Mathf.Min(baseY + image.ChunkSize, image.Height) / image.Height);
        }

        float loX = (minU - 0.5f) * WorldSizeX;
        float hiX = (maxU - 0.5f) * WorldSizeX;
        float loZ = (minV - 0.5f) * WorldSizeZ;
        float hiZ = (maxV - 0.5f) * WorldSizeZ;

        var local = new Aabb(
            new Vector3(loX, -BoundsHeight * 0.5f, loZ),
            new Vector3(hiX - loX, BoundsHeight, hiZ - loZ));
        return Entity.Transform * local;
    }

    /// <summary>
    /// <see cref="IIncrementalLandscapeDeformer.ConsumeDirtyRegions"/>: the world bounds of just the
    /// <em>pixels</em> edited since the last call, so a brush dab dirties only the terrain actually
    /// under it. Pixel-level rather than chunk-level on purpose — a default 256x256 image is one
    /// 256px chunk, so a chunk-level answer is the whole canvas, and a placement stretched over a
    /// large footprint would rebuild every landscape chunk beneath it for a single dab.
    ///
    /// Falls back to the whole <see cref="InfluenceBounds"/> whenever the image identity, strength, or
    /// channel changed instead: any of those changes what every already-painted pixel contributes
    /// regardless of which pixel it is, so narrowing would under-report.
    /// </summary>
    public IReadOnlyList<Aabb> ConsumeDirtyRegions()
    {
        if (Image is not { } image)
        {
            _dirtyTrackedImageId = ImageId;
            _dirtyTrackedSeq = -1;
            return [];
        }

        bool wide = _dirtyTrackedImageId != ImageId || _dirtyTrackedStrength != Strength || _dirtyTrackedChannel != Channel;
        _dirtyTrackedImageId = ImageId;
        _dirtyTrackedStrength = Strength;
        _dirtyTrackedChannel = Channel;

        bool any = image.TryDirtyPixelsSince(_dirtyTrackedSeq, out int minX, out int minY, out int maxX, out int maxY);
        _dirtyTrackedSeq = image.DirtySequence;

        if (wide)
        {
            return [InfluenceBounds];
        }

        return any && WorldBoundsForPixels(minX, minY, maxX, maxY) is { } bounds ? [bounds] : [];
    }

    /// <summary>The world-space AABB covering an inclusive pixel rect of the bound image — the
    /// pixel-granular counterpart to <see cref="WorldBoundsForChunks"/>. Grown by one pixel on every
    /// side, since a landscape texel bilinearly samples its neighbours and would otherwise be able to
    /// read an edited pixel from just outside the reported region.</summary>
    private Aabb? WorldBoundsForPixels(int minX, int minY, int maxX, int maxY)
    {
        if (Image is not { } image)
        {
            return null;
        }

        float u0 = (float)Mathf.Max(minX - 1, 0) / image.Width;
        float u1 = (float)Mathf.Min(maxX + 2, image.Width) / image.Width;
        float v0 = (float)Mathf.Max(minY - 1, 0) / image.Height;
        float v1 = (float)Mathf.Min(maxY + 2, image.Height) / image.Height;

        float loX = (u0 - 0.5f) * WorldSizeX;
        float hiX = (u1 - 0.5f) * WorldSizeX;
        float loZ = (v0 - 0.5f) * WorldSizeZ;
        float hiZ = (v1 - 0.5f) * WorldSizeZ;

        var local = new Aabb(
            new Vector3(loX, -BoundsHeight * 0.5f, loZ),
            new Vector3(hiX - loX, BoundsHeight, hiZ - loZ));
        return Entity.Transform * local;
    }

    private bool TryLocalToUv(Vector3 local, out float u, out float v)
    {
        u = (local.X / WorldSizeX) + 0.5f;
        v = (local.Z / WorldSizeZ) + 0.5f;
        return u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.0f;
    }
}
