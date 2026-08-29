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
}
