using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A radial landscape stamp: paints a falloff into a channel and, through the material bound to its
/// layers, raises or flattens the ground and paints a texture.
///
/// Deliberately the simplest thing that exercises the whole pipeline — claim, resolve, rasterize,
/// evaluate. It is not meant to be the tool anyone uses; splines and paintable textures come later.
/// The point is that moving this moves the terrain, because the terrain is a function of it.
/// </summary>
public sealed class StampEntity : SceneEntity, ILandscapeDeformer
{
    public string Name { get; set; } = "Stamp";

    /// <summary>Radius of influence in world units.</summary>
    public float Radius { get; set; } = 24.0f;

    /// <summary>How much of the radius fades out. 0 is a hard disc, 1 fades from the centre.</summary>
    public float Falloff { get; set; } = 0.5f;

    /// <summary>Strength written into the channel at the centre.</summary>
    public float Strength { get; set; } = 1.0f;

    /// <summary>Channel this stamp paints into, by name. Empty means it paints nothing.</summary>
    public string Channel { get; set; } = "";

    /// <summary>Texture layer claimed, by record id. Null claims no texture slot.</summary>
    public int? TextureLayerId { get; set; }

    /// <summary>Height layer claimed, by record id. Null contributes no height.</summary>
    public int? HeightLayerId { get; set; }

    /// <summary>Material bound to both claims, by record id.</summary>
    public int? MaterialId { get; set; }

    /// <summary>How important this stamp is when a chunk runs out of texture slots.</summary>
    public int Priority { get; set; }

    /// <summary>Primary key of the backing row once persisted; null until first saved.</summary>
    public int? RecordId { get; set; }

    public override string DisplayName => Name;

    /// <summary>A radial stamp looks the same at any yaw, so rotating it only moves it.</summary>
    public override SelfRotation SelfRotation => SelfRotation.None;

    public override Aabb LocalBounds => new(
        new Vector3(-Radius, -Radius, -Radius),
        new Vector3(Radius * 2.0f, Radius * 2.0f, Radius * 2.0f));

    /// <summary>
    /// Built from the persisted row id, not the runtime entity id: the resolver breaks ties on this,
    /// and a runtime id would make the same chunk resolve differently on another machine. An unsaved
    /// stamp falls back to its runtime id, which is the best identity it has yet.
    /// </summary>
    public string DeformerKey => RecordId is { } id ? $"stamp:{id}" : $"stamp:new:{Id.Value}";

    public Aabb InfluenceBounds => WorldBounds;

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context)
    {
        LandscapeMaterial? material = context.Material(MaterialId);
        var claims = new List<LandscapeClaim>();

        if (context.Layer(TextureLayerId) is { } texture)
        {
            claims.Add(new LandscapeClaim { Layer = texture, Material = material });
        }

        if (context.Layer(HeightLayerId) is { } height)
        {
            claims.Add(new LandscapeClaim { Layer = height, Material = material });
        }

        if (claims.Count == 0)
        {
            return [];
        }

        // One group: a stamp that wins its texture but loses its height (or the reverse) would be a
        // stamp doing half its job, which is what grouping exists to prevent.
        return
        [
            new LandscapeClaimGroup
            {
                Key = DeformerKey,
                Label = Name,
                Priority = Priority,
                Claims = claims,
                Source = Id,
            },
        ];
    }

    public void Rasterize(in LandscapeRasterContext context)
    {
        // A dropped stamp writes nothing. Another entity could legitimately decide otherwise — a road
        // that lost its texture may still want to cut its bed — but a stamp with no slot has no
        // business leaving its mask behind for someone else's layer to pick up.
        if (context.Resolution.IsDropped(DeformerKey))
        {
            return;
        }

        if (context.Channel(Channel) is not { } channel || context.Buffer(Channel) is not { } buffer)
        {
            return;
        }

        int resolution = channel.Resolution;
        Vector3 centre = Transform.Origin;

        // Positions are world positions, and the falloff is a function of distance from the centre,
        // so the pattern continues unbroken into whichever chunks the stamp reaches.
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 world = context.TexelCentre(resolution, x, y);
                float distance = new Vector2(world.X - centre.X, world.Z - centre.Z).Length();
                float value = Weight(distance) * Strength;

                if (value <= 0.0f)
                {
                    continue;
                }

                // Stamps accumulate rather than overwrite, so two overlapping stamps read as one shape
                // instead of one clipping the other.
                int index = (y * resolution) + x;
                buffer[index] = Mathf.Min(1.0f, buffer[index] + value);
            }
        }
    }

    private float Weight(float distance)
    {
        if (distance >= Radius)
        {
            return 0.0f;
        }

        float solid = Radius * (1.0f - Mathf.Clamp(Falloff, 0.0f, 1.0f));
        if (distance <= solid)
        {
            return 1.0f;
        }

        float fade = Radius - solid;
        return fade <= 0.0f ? 1.0f : Mathf.SmoothStep(0.0f, 1.0f, 1.0f - ((distance - solid) / fade));
    }

    protected override Node3D BuildNode()
    {
        var node = new Node3D { Name = $"Stamp{Id.Value}" };
        node.AddChild(new MeshInstance3D
        {
            Name = "Gizmo",
            Mesh = new SphereMesh { Radius = 1.5f, Height = 3.0f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.35f, 0.75f, 1.0f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });
        return node;
    }
}
