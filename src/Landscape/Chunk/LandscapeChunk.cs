using Godot;

namespace WorldMapStudio;

/// <summary>
/// One chunk of terrain in the scene. A <see cref="SceneEntity"/> so it renders, streams, appears in
/// the outline and gets an inspector for free — and an <see cref="IDerivedEntity"/> so none of that
/// makes it editable or persistable.
///
/// The way to change a chunk is to change the entities that produced it.
/// </summary>
public sealed class LandscapeChunk : SceneEntity, IDerivedEntity
{
    private readonly float _chunkSize;

    public LandscapeChunk(LandscapeChunkOutput output, LandscapeGrid grid, MapId map)
    {
        Output = output;
        _chunkSize = grid.ChunkSize;
        Map = map;
        Transform = new Transform3D(Basis.Identity, grid.OriginOf(output.Coord));
    }

    public LandscapeChunkOutput Output { get; private set; }

    public ChunkCoord Coord => Output.Coord;

    public override string DisplayName => $"Chunk {Coord}";

    /// <summary>Chunks sit on the grid; rotating one would be meaningless.</summary>
    public override SelfRotation SelfRotation => SelfRotation.None;

    public override Aabb LocalBounds => new(
        new Vector3(0.0f, -LandscapeGrid.NominalHeightExtent, 0.0f),
        new Vector3(_chunkSize, LandscapeGrid.NominalHeightExtent * 2.0f, _chunkSize));

    /// <summary>Slots the chunk resolved to, base included. Shown in the inspector.</summary>
    public int UsedSlots => Output.Layers.Count;

    /// <summary>Swaps in freshly built output and refreshes the representation if one exists.</summary>
    public void Rebuild(LandscapeChunkOutput output)
    {
        Output = output;
        if (Node is { } node)
        {
            foreach (Node child in node.GetChildren())
            {
                child.QueueFree();
            }

            node.AddChild(BuildSurface());
        }
    }

    protected override Node3D BuildNode()
    {
        var node = new Node3D { Name = $"Chunk{Coord.X}_{Coord.Y}" };
        node.AddChild(BuildSurface());
        return node;
    }

    private MeshInstance3D BuildSurface() => new()
    {
        Name = "Surface",
        Mesh = LandscapeChunkMesh.BuildMesh(Output, _chunkSize),
        MaterialOverride = LandscapeChunkMesh.BuildMaterial(Output),
    };
}
