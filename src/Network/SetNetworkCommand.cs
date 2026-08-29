using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Replaces an <see cref="INetworkEditable"/>'s network wholesale. One command for every
/// network-editing component — procedural mesh, road — since the edit is always "swap the graph".</summary>
public sealed class SetNetworkCommand : IEditCommand, IChunkChangeCommand
{
    private readonly SceneEntity _entity;
    private readonly INetworkEditable _component;
    private readonly VertexNetwork _before;
    private readonly VertexNetwork _after;

    public SetNetworkCommand(INetworkEditable component, VertexNetwork before, VertexNetwork after, string description)
    {
        _component = component;
        _entity = component.Owner ?? throw new System.InvalidOperationException("Component is not attached.");
        _before = before.Clone();
        _after = after.Clone();
        Description = description;
        Targets = new IEntity[] { _entity };

        component.ReplaceNetwork(_before);
        ChunkChangeSnapshot beforeSnapshot = ChunkChangeSnapshot.Capture(_entity);
        component.ReplaceNetwork(_after);
        ChunkChangeSnapshot afterSnapshot = ChunkChangeSnapshot.Capture(_entity);
        ChunkImpacts = [new ChunkChangeImpact(_entity, beforeSnapshot, afterSnapshot)];
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description { get; }

    public void Apply() => _component.ReplaceNetwork(_after);

    public void Revert() => _component.ReplaceNetwork(_before);
}
