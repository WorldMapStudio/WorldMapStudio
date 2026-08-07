using Godot;

namespace WorldMapStudio;

/// <summary>Placeholder scene entity: a single coloured, optionally yawed box. Demo content until
/// real entities exist, kept so the viewport has something to select and transform.</summary>
public sealed class DemoBoxEntity : SceneEntity
{
    private static readonly Vector3 BoxSize = new(2.0f, 2.0f, 2.0f);

    private readonly Color _color;

    public DemoBoxEntity(Vector3 position, Color color, float yaw)
    {
        _color = color;
        Transform = new Transform3D(new Basis(Vector3.Up, yaw), position);
    }

    public override SelfRotation SelfRotation => SelfRotation.Full;

    public override Aabb LocalBounds => new(-BoxSize * 0.5f, BoxSize);

    protected override Node3D BuildNode()
    {
        var node = new Node3D { Name = $"DemoBox{Id.Value}" };
        node.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = BoxSize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = _color,
                Metallic = 0.2f,
                Roughness = 0.55f,
            },
        });
        return node;
    }
}
