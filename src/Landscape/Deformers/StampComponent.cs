using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

public sealed class StampComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent, ITransformPolicy, ILandscapeDeformer
{
    public float Radius { get; set; } = 24.0f;

    public float Falloff { get; set; } = 0.5f;

    public float Strength { get; set; } = 1.0f;

    public string Channel { get; set; } = "";

    public int? LayerId { get; set; }

    public int? MaterialId { get; set; }

    public int Priority { get; set; }

    public override string TypeId => "landscape-stamp";

    public override string DisplayName => "Landscape Stamp";

    public SelfRotation SelfRotation => SelfRotation.None;

    public bool UsesTerrainHeight => false;

    public Aabb LocalBounds => new(
        new Vector3(-Radius, -Radius, -Radius),
        new Vector3(Radius * 2.0f, Radius * 2.0f, Radius * 2.0f));

    public string DeformerKey => Entity.RecordId is { } id
        ? $"entity:{id}:stamp"
        : $"entity:new:{Entity.Id.Value}:stamp";

    public Aabb InfluenceBounds => Entity.Transform * LocalBounds;

    public override int ContentVersion =>
        System.HashCode.Combine(Radius, Falloff, Strength, Channel, LayerId, MaterialId, Priority);

    public IEnumerable<LandscapeClaimGroup> Claim(in LandscapeClaimContext context)
    {
        if (context.Layer(LayerId) is not { } layer)
        {
            return [];
        }

        return
        [
            new LandscapeClaimGroup
            {
                Key = DeformerKey,
                Label = Entity.DisplayName,
                Priority = Priority,
                Claims = [new LandscapeClaim { Layer = layer, Material = context.Material(MaterialId) }],
                Source = Entity.Id,
            },
        ];
    }

    public void Rasterize(in LandscapeRasterContext context)
    {
        if (context.Resolution.IsDropped(DeformerKey))
        {
            return;
        }

        if (context.Channel(Channel) is not { } channel || context.Buffer(Channel) is not { } buffer)
        {
            return;
        }

        int resolution = channel.Resolution;
        Vector3 centre = Entity.Transform.Origin;

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

                int index = (y * resolution) + x;
                buffer[index] = Mathf.Min(1.0f, buffer[index] + value);
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
