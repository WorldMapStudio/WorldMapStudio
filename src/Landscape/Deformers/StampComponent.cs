using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public sealed class StampComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, ITransformPolicy, ILandscapeDeformer
{
    public float Radius { get; set; } = 24.0f;

    public float Falloff { get; set; } = 0.5f;

    public float Strength { get; set; } = 1.0f;

    public string Channel { get; set; } = "";

    /// <summary>Only meaningful when the bound channel carries more than one component — see
    /// <see cref="Rasterize"/>. Ignored on a scalar channel, where <see cref="Strength"/> alone
    /// decides how much this stamp adds.</summary>
    public Color ColorValue { get; set; } = Colors.White;

    public override string TypeId => "landscape-stamp";

    public override string DisplayName => "Landscape Stamp";

    public SelfRotation SelfRotation => SelfRotation.None;

    public SelfScale SelfScale => SelfScale.None;

    public bool UsesTerrainHeight => false;

    public Aabb LocalBounds => new(
        new Vector3(-Radius, -Radius, -Radius),
        new Vector3(Radius * 2.0f, Radius * 2.0f, Radius * 2.0f));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:stamp"
        : $"entity:new:{Entity.Id.Value}:stamp";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion =>
        System.HashCode.Combine(Radius, Falloff, Strength, Channel, ColorValue);

    public override SceneComponent Clone() => new StampComponent
    {
        Radius = Radius,
        Falloff = Falloff,
        Strength = Strength,
        Channel = Channel,
        ColorValue = ColorValue,
    };

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context) => [];

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (context.Channel(Channel) is not { } channel || context.Writer(Channel) is not { } writer)
        {
            return;
        }

        int resolution = channel.Resolution;
        Vector3 centre = Entity.Transform.Origin;
        bool writeColor = writer.Components > 1;

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

                if (writeColor)
                {
                    Color current = writer.GetColor(x, y);
                    writer.SetColor(x, y, new Color(
                        Mathf.Min(1.0f, current.R + (ColorValue.R * value)),
                        Mathf.Min(1.0f, current.G + (ColorValue.G * value)),
                        Mathf.Min(1.0f, current.B + (ColorValue.B * value)),
                        Mathf.Min(1.0f, current.A + (ColorValue.A * value))));
                }
                else
                {
                    writer.Set(x, y, Mathf.Min(1.0f, writer.Get(x, y) + value));
                }
            }
        }
    }

    public Node3D BuildNode()
    {
        var node = new Node3D { Name = "StampComponent" };
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
}
