using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// One field going from one value to another on a shared <see cref="ProceduralModel"/> — its
/// function, parameters, output format or material bindings.
///
/// The counterpart of <see cref="SetFieldCommand{T}"/> for data that lives on a catalog model rather
/// than a scene entity: the field is set on the model, but the chunk impact is captured across every
/// loaded placement of it, exactly as <see cref="SetNetworkCommand"/> already does for the network.
/// A plain <see cref="SetFieldCommand{T}"/> here would report no impact at all — the model is not a
/// <see cref="SceneEntity"/> — and an export would never see the placements move.
/// </summary>
public sealed class SetProceduralModelFieldCommand<T> : IEditCommand, IChunkChangeCommand
{
    private readonly ProceduralModel _model;
    private readonly string _field;
    private readonly Action<T> _set;
    private readonly T _before;
    private readonly T _after;

    public SetProceduralModelFieldCommand(ProceduralSystem system, ProceduralModel model, string field, Action<T> set, T before, T after)
    {
        _model = model;
        _field = field;
        _set = set;
        _before = before;
        _after = after;
        Targets = new IEntity[] { model };

        List<SceneEntity> placements = system.PlacementsOf(model).ToList();
        set(before);
        Dictionary<SceneEntity, ChunkChangeSnapshot> beforeSnapshots = Capture(placements);
        set(after);
        Dictionary<SceneEntity, ChunkChangeSnapshot> afterSnapshots = Capture(placements);

        ChunkImpacts = placements
            .Select(entity => new ChunkChangeImpact(
                entity,
                beforeSnapshots.GetValueOrDefault(entity),
                afterSnapshots.GetValueOrDefault(entity)))
            .ToList();
    }

    public IReadOnlyList<IEntity> Targets { get; }

    public IReadOnlyList<ChunkChangeImpact> ChunkImpacts { get; }

    public string Description => $"Set {_field} on {_model.DisplayName}";

    public void Apply() => _set(_after);

    public void Revert() => _set(_before);

    private static Dictionary<SceneEntity, ChunkChangeSnapshot> Capture(IEnumerable<SceneEntity> entities) =>
        entities.ToDictionary(entity => entity, entity => ChunkChangeSnapshot.Capture(entity));
}
