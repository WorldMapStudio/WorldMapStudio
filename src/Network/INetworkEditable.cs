using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// A component whose authored shape is a <see cref="VertexNetwork"/>, edited in the viewport by the
/// shared <see cref="NetworkEditTool"/>. A procedural mesh's skeleton and a road's centreline both
/// implement this and nothing else network-specific — what the network is *for* is entirely up to the
/// component.
/// </summary>
public interface INetworkEditable
{
    VertexNetwork Network { get; }

    /// <summary>Replaces the network wholesale, cloning it so the caller's copy stays independent.</summary>
    void ReplaceNetwork(VertexNetwork network);

    SceneEntity? Owner { get; }

    /// <summary>
    /// Whether this network is authored flat: vertices carry no meaningful height, so the tool drops
    /// Y on every move/scale, rotates only about Y, and places new vertices on the terrain under the
    /// cursor instead of on the entity's local plane.
    /// </summary>
    bool PlanarXZ { get; }

    /// <summary>
    /// Whether a new vertex should snap onto the terrain surface (keeping its real sampled height) when
    /// placed, even though the network as a whole is not <see cref="PlanarXZ"/> — a fence's control
    /// points, for instance, which should start out sitting on the ground rather than on the entity's
    /// local plane. Defaults to false for every network that has no reason to care.
    /// </summary>
    bool SnapToTerrainOnPlace => false;

    /// <summary>
    /// What an edit session pins and persists a network edit against — a road's own entity, but a
    /// procedural mesh's <em>model</em> rather than the placement being edited, since the network
    /// lives on the model and may be shared by other placements. Defaults to <see cref="Owner"/> for
    /// components (like a road) where the network is not shared.
    /// </summary>
    IEntity EditTarget => Owner ?? throw new System.InvalidOperationException("Component is not attached.");

    /// <summary>
    /// Every loaded scene entity whose rendered result this network feeds, for chunk-change capture —
    /// just <see cref="Owner"/> for a component with its own network, but every loaded placement of a
    /// shared model. Defaults to <see cref="Owner"/> alone.
    /// </summary>
    IEnumerable<SceneEntity> AffectedEntities => Owner is { } owner ? Enumerable.Repeat(owner, 1) : [];

    /// <summary>
    /// Immutable stroke snapshot the viewport tool draws as a shape preview instead of the raw graph
    /// edges — a road's spline and width, for instance, which an edge-only preview would lie about.
    /// Empty (the default) for a network whose edges already are the shape.
    /// </summary>
    ProceduralPaint Paint => ProceduralPaint.Empty;
}
