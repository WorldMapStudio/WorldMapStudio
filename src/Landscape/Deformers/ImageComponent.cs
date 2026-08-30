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
public sealed class ImageComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer, ISceneNodeComponent, IMeshPickable
{
    private const float BoundsHeight = 2.0f;

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

    public ImageComponent(ImageSystem system)
    {
        // Guarded for the same reason ProceduralComponent guards its system: a null here is otherwise
        // invisible until first rasterized, then surfaces as a NullReferenceException deep in a
        // landscape build with no hint the real mistake was made back at construction time.
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

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

    public float Strength { get; set; } = 1.0f;

    public string Channel { get; set; } = "";

    public override string TypeId => "image";

    public override string DisplayName => "Image";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public SelfScale SelfScale => SelfScale.None;

    // Free height: neither Rasterize nor Paint below ever read local.Y, so an entity's Y position has
    // no bearing on the terrain projection — only rotation does (see SelfRotation above), which is
    // why this is the only one of the two that's unlocked. StampComponent already establishes that
    // "free height, still an ILandscapeDeformer" is a safe combination in this codebase.
    public bool UsesTerrainHeight => false;

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
    /// representation was last built.</summary>
    public bool NeedsRefresh =>
        _representedImageId != ImageId || _representedImageRevision != (Image?.ViewRevision ?? -1) ||
        _representedDisplayLayerId != DisplayLayerId || _representedDisplayLayerRevision != (DisplayLayer?.Revision ?? -1);

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

        if (DisplayLayer is not { } layer || Image is not { } image || layer.DisplayMode == ImageDisplayMode.None)
        {
            return null;
        }

        return layer.DisplayMode switch
        {
            ImageDisplayMode.LandscapeOverlay => BuildOverlayDecal(image, layer),
            ImageDisplayMode.Object => BuildObjectMesh(image),
            _ => null,
        };
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
            if (ChunkPlacementFor(image, coord) is not { } placement)
            {
                continue;
            }

            root.AddChild(new Decal
            {
                Name = $"Chunk_{coord.X}_{coord.Y}",
                Position = new Vector3(placement.CenterX, 0.0f, placement.CenterZ),
                Size = new Vector3(placement.SizeX, LandscapeGrid.NominalHeightExtent * 2.0f, placement.SizeZ),
                TextureAlbedo = PaintImageTextures.ChunkTinted(image, coord, layer.BaseColor, layer.FullColor),
            });
        }

        return root;
    }

    /// <summary>A flat, unshaded backdrop sized to the whole footprint — solid black, no texture, so
    /// its cost does not scale with canvas size — with one further quad layered per resident chunk
    /// showing that chunk's own grayscale texture. The backdrop is what the Paint tool targets directly
    /// (via <see cref="SceneEntity.TryPickGeometry"/>) instead of projecting through the terrain when
    /// this placement's display mode is <see cref="ImageDisplayMode.Object"/>: it exists even before
    /// anything has been painted, so there is always something to click and start painting on.</summary>
    private Node3D BuildObjectMesh(PaintImage image)
    {
        var root = new Node3D { Name = "ImageObject" };
        root.AddChild(new MeshInstance3D
        {
            Name = "Backdrop",
            Mesh = new PlaneMesh { Size = new Vector2(WorldSizeX, WorldSizeZ) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });

        foreach (ImageChunkCoord coord in image.ChunkCoords)
        {
            if (ChunkPlacementFor(image, coord) is not { } placement)
            {
                continue;
            }

            root.AddChild(new MeshInstance3D
            {
                Name = $"Chunk_{coord.X}_{coord.Y}",
                // A hair above the backdrop so the two flat, coplanar quads do not z-fight.
                Position = new Vector3(placement.CenterX, 0.001f, placement.CenterZ),
                Mesh = new PlaneMesh { Size = new Vector2(placement.SizeX, placement.SizeZ) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoTexture = PaintImageTextures.ChunkGrayscale(image, coord),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            });
        }

        return root;
    }

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

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (Image is not { } image)
        {
            return;
        }

        if (context.Channel(Channel) is not { } channel || context.Buffer(Channel) is not { } buffer)
        {
            return;
        }

        int resolution = channel.Resolution;
        Transform3D inverse = Entity.Transform.AffineInverse();

        // Snapshotted once per rasterize rather than sampled straight off the image: this runs on a
        // landscape build worker while the image may be being painted concurrently on the main
        // thread — see ImageChunkTable for why a snapshot is what makes that safe.
        ImageSampler sampler = image.CreateSampler();

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 local = inverse * context.TexelCentre(resolution, x, y);
                if (!TryLocalToUv(local, out float u, out float v))
                {
                    continue;
                }

                float value = sampler.Sample(u, v) * Strength;
                if (value <= 0.0f)
                {
                    continue;
                }

                int index = (y * resolution) + x;
                buffer[index] = Mathf.Min(1.0f, Mathf.Max(buffer[index], value));
            }
        }
    }

    /// <summary>Stamps a soft circular brush at a local-space point, in the bound image's pixels.
    /// False if unbound or the point falls outside this placement's footprint.</summary>
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

    private bool TryLocalToUv(Vector3 local, out float u, out float v)
    {
        u = (local.X / WorldSizeX) + 0.5f;
        v = (local.Z / WorldSizeZ) + 0.5f;
        return u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.0f;
    }
}
