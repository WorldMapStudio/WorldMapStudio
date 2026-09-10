using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The render unit for terrain: one mesh, one material and one alpha texture array for a
/// <c>B × B</c> square of chunks. The chunk stays the data/authoring/export unit — a batch is only
/// how chunks are grouped for drawing, driven by a view setting — so at <c>B = 1</c> this is one
/// batch per chunk, exactly what the editor did before batching existed.
///
/// A <see cref="SceneEntity"/> so it renders and streams, and an <see cref="IDerivedEntity"/> so none
/// of that makes it editable or persistable. The way to change terrain is to change the entities that
/// produced it.
/// </summary>
public sealed class LandscapeTerrainBatch : SceneEntity, IDerivedEntity
{
    /// <summary>Upper bound on the batch-size view setting.</summary>
    public const int MaxTerrainBatchChunks = 16;

    /// <summary>The shader's compile-time slot loop bound.</summary>
    public const int MaxSlots = 16;

    /// <summary>The visual layer terrain surfaces render on, distinct from the default layer every
    /// other node stays on. What lets <see cref="ImageComponent"/>'s LandscapeOverlay
    /// <see cref="Decal"/> restrict its <see cref="Decal.CullMask"/> to actual terrain.</summary>
    public const uint RenderLayer = 1u << 1;

    private readonly Aabb _localBounds;

    public LandscapeTerrainBatch(
        LandscapeBatchCoord batchCoord,
        int batchChunks,
        IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> chunks,
        ArrayMesh mesh,
        ShaderMaterial material,
        LandscapeGrid grid,
        MapId map)
    {
        BatchCoord = batchCoord;
        BatchChunks = batchChunks;
        Chunks = chunks;
        Mesh = mesh;
        Material = material;

        // A nominal ±NominalHeightExtent slab BatchChunks chunks on a side, until real heights are
        // known — same reasoning as the per-chunk bounds it replaces.
        float span = batchChunks * grid.ChunkSize;
        _localBounds = new Aabb(
            new Vector3(0.0f, -LandscapeGrid.NominalHeightExtent, 0.0f),
            new Vector3(span, LandscapeGrid.NominalHeightExtent * 2.0f, span));

        Map = map;
        Transform = new Transform3D(Basis.Identity, grid.OriginOf(batchCoord.Origin(batchChunks)));
    }

    public LandscapeBatchCoord BatchCoord { get; }

    public int BatchChunks { get; }

    /// <summary>The chunk outputs this batch draws, by coordinate.</summary>
    public IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> Chunks { get; private set; }

    /// <summary>The surface mesh currently built for <see cref="Chunks"/>.</summary>
    public ArrayMesh Mesh { get; private set; }

    /// <summary>The splatting material currently built for <see cref="Chunks"/>.</summary>
    public ShaderMaterial Material { get; private set; }

    public override string DisplayName => $"Terrain {BatchCoord.X},{BatchCoord.Y}";

    /// <summary>Batches sit on the grid; rotating or scaling one would be meaningless.</summary>
    public override SelfRotation SelfRotation => SelfRotation.None;

    public override SelfScale SelfScale => SelfScale.None;

    public override Aabb LocalBounds => _localBounds;

    /// <summary>Swaps in freshly built chunks — plus the mesh and material already built for them —
    /// and refreshes the representation if one exists.</summary>
    public void Rebuild(
        IReadOnlyDictionary<ChunkCoord, LandscapeChunkOutput> chunks,
        ArrayMesh mesh,
        ShaderMaterial material)
    {
        ArrayMesh previousMesh = Mesh;
        ShaderMaterial previousMaterial = Material;

        Chunks = chunks;
        Mesh = mesh;
        Material = material;

        if (Node is { } node)
        {
            if (node.GetChildOrNull<MeshInstance3D>(0) is { } surface)
            {
                // A couple of RID writes rather than tearing the node down and churning a
                // MeshInstance3D on every rebuild.
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

        if (!ReferenceEquals(previousMesh, mesh))
        {
            previousMesh.Dispose();
        }

        if (!ReferenceEquals(previousMaterial, material))
        {
            DisposeMaterial(previousMaterial);
        }
    }

    /// <summary>
    /// Frees the mesh and material this batch owns. Streaming unloads batches continuously as the view
    /// moves, and Godot resources left to finalization are a queue a single thread drains — at a real
    /// view distance that queue is what the load ends up waiting behind.
    /// </summary>
    public override void Unload()
    {
        Mesh.Dispose();
        DisposeMaterial(Material);
    }

    // Cached rather than converted from a string literal per call: each conversion is a finalizable
    // StringName, and material disposal runs at streaming rates.
    private static readonly StringName SlotAlphaParam = "slot_alpha";
    private static readonly StringName SlotMapParam = "slot_map";
    private static readonly StringName ShowChunkEdgesParam = "show_chunk_edges";

    /// <summary>Frees a batch material with its per-batch alpha array and slot-map texture. The albedo
    /// and height arrays come from <see cref="LandscapeBatchMesh"/>'s shared caches and must outlive
    /// any one batch, so they are deliberately left alone.</summary>
    public static void DisposeMaterial(ShaderMaterial material)
    {
        if (material.GetShaderParameter(SlotAlphaParam).As<Texture2DArray>() is { } alpha)
        {
            alpha.Dispose();
        }

        if (material.GetShaderParameter(SlotMapParam).As<Texture2D>() is { } slotMap)
        {
            slotMap.Dispose();
        }

        material.Dispose();
    }

    /// <summary>Toggles the chunk border overlay on this batch's existing material.</summary>
    public void SetChunkEdgesVisible(bool visible)
    {
        if (Node?.GetChildOrNull<MeshInstance3D>(0) is { MaterialOverride: ShaderMaterial material })
        {
            material.SetShaderParameter(ShowChunkEdgesParam, visible);
        }
    }

    /// <summary>
    /// Registers the surface for click picking: <see cref="LocalBounds"/> is a nominal slab far taller
    /// than the terrain it wraps, so picking by bounds would claim every click made from inside it.
    /// </summary>
    protected override Node3D BuildNode()
    {
        ClearPickNodes();
        var node = new Node3D { Name = $"TerrainBatch{BatchCoord.X}_{BatchCoord.Y}" };
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
