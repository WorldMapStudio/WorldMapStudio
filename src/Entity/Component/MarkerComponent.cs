using System;
using Godot;

namespace WorldMapStudio;

/// <summary>The display shape drawn for a marker component, like Blender's empty types.</summary>
public enum MarkerShape
{
    Plain,
    Cube,
    Sphere,
}

public sealed class MarkerComponent : SceneComponent, ISceneBoundsProvider, ISceneNodeComponent
{
    private static readonly Vector3 MarkerSize = new(1.0f, 1.0f, 1.0f);

    public MarkerShape Shape { get; set; } = MarkerShape.Plain;

    public override string TypeId => "marker";

    public override string DisplayName => "Marker";

    public override int ContentVersion => HashCode.Combine(Shape);

    public Aabb LocalBounds => new(-MarkerSize * 0.5f, MarkerSize);

    public override SceneComponent Clone() => new MarkerComponent { Shape = Shape };

    public Node3D BuildNode()
    {
        var node = new Node3D { Name = "MarkerComponent" };
        node.AddChild(new MeshInstance3D
        {
            Name = "Marker",
            Mesh = BuildMesh(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.85f, 0.35f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });
        return node;
    }

    private Mesh BuildMesh() => Shape switch
    {
        MarkerShape.Cube => new BoxMesh { Size = MarkerSize * 0.5f },
        MarkerShape.Sphere => new SphereMesh { Radius = 0.3f, Height = 0.6f },
        _ => new SphereMesh { Radius = 0.1f, Height = 0.2f },
    };
}
