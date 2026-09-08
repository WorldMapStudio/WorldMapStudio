using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Replaces an <see cref="INetworkEditable"/>'s network wholesale. One command for every
/// network-editing component — procedural mesh, road — since the edit is always "swap the graph".
///
/// Snapshots every one of <see cref="INetworkEditable.AffectedEntities"/>, not just the entity the
/// tool happened to be pointed at: a procedural mesh's network lives on its bound model, which other
/// placements may share, so every placement's chunk fingerprint can move even though only one of them
/// was clicked.</summary>
public sealed class SetNetworkCommand : IEditCommand, IChunkChangeCommand, ISharedResourceChunkCommand
{
    private readonly INetworkEditable _component;
    private readonly VertexNetwork _before;
    private readonly VertexNetwork _after;

    public SetNetworkCommand(INetworkEditable component, VertexNetwork before, VertexNetwork after, string description)
    {
        _component = component;
        _before = before.Clone();
        _after = after.Clone();
        Description = description;
        Targets = new IEntity[] { component.EditTarget };

        component.ReplaceNetwork(_before);
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots = CaptureAll(component);
        component.ReplaceNetwork(_after);
        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots = CaptureAll(component);

        ChunkImpacts = afterSnapshots
            .Select(pair => new ChunkChangeImpact(
                pair.Key,
                beforeSnapshots.GetValueOrDefault(pair.Key),
                pair.Value))
            .ToList();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    // Only set when the network lives on a shared catalog resource (a procedural mesh's model), not a
    // per-entity one (a road), so RecordCommit restamps the model's streamed-out placements too.
    public (Type Type, int Id)? SharedResource =>
        _component.EditTarget is IKeyedCatalogEntity { RecordId: int id } resource
            ? (resource.GetType(), id)
            : null;

    public string Description { get; }

    public void Apply() => _component.ReplaceNetwork(_after);

    public void Revert() => _component.ReplaceNetwork(_before);

    private static Dictionary<SceneEntity, ChunkChangeSnapshot> CaptureAll(INetworkEditable component) =>
        component.AffectedEntities.ToDictionary(entity => entity, entity => ChunkChangeSnapshot.Capture(entity));
}
