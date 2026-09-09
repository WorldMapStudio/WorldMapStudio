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
    /// <summary>The visual layer terrain surfaces render on, distinct from the default layer every
    /// other node (models, markers, the viewport's reference grid) stays on. What lets
    /// <see cref="ImageComponent"/>'s LandscapeOverlay <see cref="Decal"/> restrict its
    /// <see cref="Decal.CullMask"/> to actual terrain — a decal's default cull mask matches everything
    /// on the default layer, which is every other kind of geometry in the scene too.</summary>
    public const uint RenderLayer = 1u << 1;

    private readonly float _chunkSize;
    private readonly Aabb _localBounds;

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
        _localBounds = new Aabb(
            new Vector3(0.0f, -LandscapeGrid.NominalHeightExtent, 0.0f),
            new Vector3(_chunkSize, LandscapeGrid.NominalHeightExtent * 2.0f, _chunkSize));
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

    public override Aabb LocalBounds => _localBounds;

    /// <summary>Slots the chunk resolved to, base included. Shown in the inspector.</summary>
    public int UsedSlots => Output.Layers.Count;

    /// <summary>Swaps in freshly built output — plus the mesh and material already built for it — and
    /// refreshes the representation if one exists.</summary>
    public void Rebuild(LandscapeChunkOutput output, ArrayMesh mesh, ShaderMaterial material)
    {
        ArrayMesh previousMesh = Mesh;
        ShaderMaterial previousMaterial = Material;

        Output = output;
        Mesh = mesh;
        Material = material;

        if (Node is { } node)
        {
            if (node.GetChildOrNull<MeshInstance3D>(0) is { } surface)
            {
                // Swapping the existing surface's mesh and material is a couple of RID writes; tearing
                // the node down and rebuilding it churned a MeshInstance3D on every dirtied chunk of
                // every rebuild wave, which is what the finalizer thread was spending its time on.
                surface.Mesh = mesh;
                surface.MaterialOverride = material;
            }
            else
            {
                ClearPickNodes();
                MeshInstance3D built = BuildSurface();
                node.AddChild(built);
                RegisterPickNode(built);
            }
        }

        // Nothing references the old pair any more; free their RIDs now instead of leaving a whole
        // wave's worth of them to finalization.
        if (!ReferenceEquals(previousMesh, mesh))
        {
            previousMesh.Dispose();
        }

        if (!ReferenceEquals(previousMaterial, material))
        {
            DisposeChunkMaterial(previousMaterial);
        }
    }

    /// <summary>
    /// Frees the mesh and material this chunk owns. Streaming unloads chunks continuously as the view
    /// moves, and one chunk's worth of Godot resources left to finalization is several objects on a
    /// queue a single thread drains — at a real view distance that queue is what the load ends up
    /// waiting behind.
    /// </summary>
    public override void Unload()
    {
        Mesh.Dispose();
        DisposeChunkMaterial(Material);
    }

    // Frees a replaced chunk material along with its per-chunk alpha array. The albedo and height
    // arrays are shared through LandscapeChunkMesh's caches and must outlive any one chunk, so they
    // are deliberately left alone.
    private static void DisposeChunkMaterial(ShaderMaterial material)
    {
        if (material.GetShaderParameter("slot_alpha").As<Texture2DArray>() is { } alpha)
        {
            alpha.Dispose();
        }

        material.Dispose();
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
        Layers = RenderLayer,
    };
}
