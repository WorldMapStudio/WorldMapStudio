using System.Collections.Generic;

namespace WorldMapStudio;

public sealed class SetProceduralMeshNetworkCommand : IEditCommand, IChunkChangeCommand
{
    private readonly SceneEntity _entity;
    private readonly ProceduralMeshComponent _component;
    private readonly ProceduralMeshNetwork _before;
    private readonly ProceduralMeshNetwork _after;

    public SetProceduralMeshNetworkCommand(ProceduralMeshComponent component, ProceduralMeshNetwork before, ProceduralMeshNetwork after, string description)
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
