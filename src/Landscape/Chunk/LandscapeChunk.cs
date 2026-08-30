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

    /// <summary>
    /// Takes the mesh and material already built for <paramref name="output"/>, rather than building
    /// them itself: <see cref="LandscapeChunkMesh"/>'s work is real Godot resource construction
    /// (texture decode, mesh upload) and callers build it on the background thread the chunk data
    /// itself came from, so this constructor — reachable from a streaming scan's continuation — never
    /// does that work on whichever thread happens to call it.
    /// </summary>
    public LandscapeChunk(LandscapeChunkOutput output, ArrayMesh mesh, ShaderMaterial material, LandscapeGrid grid, MapId map)
    {
        Output = output;
        Mesh = mesh;
        Material = material;
        _chunkSize = grid.ChunkSize;
        Map = map;
        Transform = new Transform3D(Basis.Identity, grid.OriginOf(output.Coord));
    }

    public LandscapeChunkOutput Output { get; private set; }

    /// <summary>The surface mesh currently built for <see cref="Output"/>.</summary>
    public ArrayMesh Mesh { get; private set; }

    /// <summary>The splatting material currently built for <see cref="Output"/>.</summary>
    public ShaderMaterial Material { get; private set; }

    public ChunkCoord Coord => Output.Coord;

    public override string DisplayName => $"Chunk {Coord}";

    /// <summary>Chunks sit on the grid; rotating or scaling one would be meaningless.</summary>
    public override SelfRotation SelfRotation => SelfRotation.None;

    public override SelfScale SelfScale => SelfScale.None;

    public override Aabb LocalBounds => new(
        new Vector3(0.0f, -LandscapeGrid.NominalHeightExtent, 0.0f),
        new Vector3(_chunkSize, LandscapeGrid.NominalHeightExtent * 2.0f, _chunkSize));

    /// <summary>Slots the chunk resolved to, base included. Shown in the inspector.</summary>
    public int UsedSlots => Output.Layers.Count;

    /// <summary>Swaps in freshly built output — plus the mesh and material already built for it — and
    /// refreshes the representation if one exists.</summary>
    public void Rebuild(LandscapeChunkOutput output, ArrayMesh mesh, ShaderMaterial material)
    {
        Output = output;
        Mesh = mesh;
        Material = material;
        if (Node is not { } node)
        {
            return;
        }

        // Detached before the replacement goes in: QueueFree only takes effect at the end of the
        // frame, so leaving the old surface attached would z-fight with the new one for a frame.
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }

        ClearPickNodes();
        MeshInstance3D surface = BuildSurface();
        node.AddChild(surface);
        RegisterPickNode(surface);
    }

    /// <summary>Toggles the chunk border overlay on this chunk's existing material.</summary>
    public void SetChunkEdgesVisible(bool visible)
    {
        if (Node?.GetChildOrNull<MeshInstance3D>(0) is { MaterialOverride: ShaderMaterial material })
        {
            material.SetShaderParameter("show_chunk_edges", visible);
        }
    }

    /// <summary>
    /// Registers the surface for click picking: <see cref="LocalBounds"/> is a nominal
    /// ±<see cref="LandscapeGrid.NominalHeightExtent"/> slab, far taller than the terrain it wraps, so
    /// picking a chunk by its bounds would claim every click made from inside that slab.
    /// </summary>
    protected override Node3D BuildNode()
    {
        ClearPickNodes();
        var node = new Node3D { Name = $"Chunk{Coord.X}_{Coord.Y}" };
        MeshInstance3D surface = BuildSurface();
        node.AddChild(surface);
        RegisterPickNode(surface);
        return node;
    }

    private MeshInstance3D BuildSurface() => new()
    {
        Name = "Surface",
        Mesh = Mesh,
        MaterialOverride = Material,
    };
}
