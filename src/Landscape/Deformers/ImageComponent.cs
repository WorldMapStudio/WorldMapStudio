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
public sealed class ImageComponent : SceneComponent, ISceneBoundsProvider, ITransformPolicy, ILandscapeDeformer
{
    private const float BoundsHeight = 2.0f;

    private readonly ImageSystem _system;
    private int? _imageId;
    private float _worldSizeX = 64.0f;
    private float _worldSizeZ = 64.0f;

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
        set => _imageId = value;
    }

    /// <summary>The bound image, or null if <see cref="ImageId"/> is unset or dangling.</summary>
    public PaintImage? Image => _system.FindImage(_imageId);

    public float WorldSizeX
    {
        get => _worldSizeX;
        set => _worldSizeX = Mathf.Max(0.5f, value);
    }

    public float WorldSizeZ
    {
        get => _worldSizeZ;
        set => _worldSizeZ = Mathf.Max(0.5f, value);
    }

    public float Strength { get; set; } = 1.0f;

    public string Channel { get; set; } = "";

    public override string TypeId => "image";

    public override string DisplayName => "Image";

    public SelfRotation SelfRotation => SelfRotation.HeightOnly;

    public SelfScale SelfScale => SelfScale.None;

    public bool UsesTerrainHeight => true;

    public Aabb LocalBounds => new(
        new Vector3(-WorldSizeX * 0.5f, -BoundsHeight * 0.5f, -WorldSizeZ * 0.5f),
        new Vector3(WorldSizeX, BoundsHeight, WorldSizeZ));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:image"
        : $"entity:new:{Entity.Id.Value}:image";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion => HashCode.Combine(ImageId, Image?.Revision ?? 0, WorldSizeX, WorldSizeZ, Strength, Channel);

    /// <summary>Every loaded placement referencing the same image — what a paint or resize undo
    /// command snapshots chunk fingerprints against, since the edit lands on the shared image, not
    /// this placement alone.</summary>
    public IEnumerable<SceneEntity> AffectedEntities => _imageId is int id
        ? _system.Context.Scene.Entities.Where(entity => entity.Component<ImageComponent>()?.ImageId == id)
        : Owner is { } owner ? [owner] : [];

    /// <summary>An independent placement of the same image — cloning a component shares its bound
    /// image rather than forking the pixels, the same way cloning a <see cref="ProceduralComponent"/>
    /// shares its bound model.</summary>
    public override SceneComponent Clone() => new ImageComponent(_system)
    {
        ImageId = ImageId,
        WorldSizeX = WorldSizeX,
        WorldSizeZ = WorldSizeZ,
        Strength = Strength,
        Channel = Channel,
    };

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

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 local = inverse * context.TexelCentre(resolution, x, y);
                if (!TryLocalToUv(local, out float u, out float v))
                {
                    continue;
                }

                float value = image.Sample(u, v) * Strength;
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

    private bool TryLocalToUv(Vector3 local, out float u, out float v)
    {
        u = (local.X / WorldSizeX) + 0.5f;
        v = (local.Z / WorldSizeZ) + 0.5f;
        return u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.0f;
    }
}
