using Godot;

namespace WorldMapStudio;

/// <summary>The display shape drawn for an <see cref="EmptyEntity"/>, like Blender's empty types.</summary>
public enum EmptyShape
{
    Plain,
    Cube,
    Sphere,
}

/// <summary>
/// A Blender-style "empty": a lightweight positional marker with an optional display shape and no
/// real geometry. The first built-in persisted scene entity, managed by the Editor storage.
/// </summary>
public sealed class EmptyEntity : SceneEntity
{
    private static readonly Vector3 MarkerSize = new(1.0f, 1.0f, 1.0f);

    [ScriptProperty(Mutable = true)]
    public string Name { get; set; } = "Empty";

    public EmptyShape Shape { get; set; } = EmptyShape.Plain;

    /// <summary>Primary key of the backing row once persisted; null until first saved.</summary>
    public int? RecordId { get; set; }

    public override string DisplayName => Name;

    public override SelfRotation SelfRotation => SelfRotation.Full;

    public override Aabb LocalBounds => new(-MarkerSize * 0.5f, MarkerSize);

    protected override Node3D BuildNode()
    {
        var node = new Node3D { Name = $"Empty{Id.Value}" };
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
        EmptyShape.Cube => new BoxMesh { Size = MarkerSize * 0.5f },
        EmptyShape.Sphere => new SphereMesh { Radius = 0.3f, Height = 0.6f },
        _ => new SphereMesh { Radius = 0.1f, Height = 0.2f },
    };
}
