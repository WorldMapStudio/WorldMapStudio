using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Sets the tags of a set of scene entities from one set of values to another — one undo step whether
/// it tags one entity or three thousand. Each entity has its own before-set, so a bulk add leaves what
/// every entity already carried alone.
/// </summary>
public sealed class SetEntityTagsCommand : IEditCommand
{
    private readonly SceneEntityRegistry _scene;
    private readonly SceneEntity[] _entities;
    private readonly EntityTagSet[] _before;
    private readonly EntityTagSet[] _after;

    public SetEntityTagsCommand(SceneEntityRegistry scene, SceneEntity[] entities, EntityTagSet[] before, EntityTagSet[] after)
    {
        _scene = scene;
        _entities = entities;
        _before = before;
        _after = after;
    }

    public IReadOnlyList<IEntity> Targets => _entities;

    public string Description => _entities.Length == 1
        ? $"Set tags on {_entities[0].DisplayName}"
        : $"Set tags on {_entities.Length} entities";

    public void Apply()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _scene.SetTags(_entities[i], _after[i]);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _scene.SetTags(_entities[i], _before[i]);
        }
    }
}
