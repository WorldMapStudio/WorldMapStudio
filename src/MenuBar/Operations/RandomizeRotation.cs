using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>Randomly yaws each selected <see cref="SceneEntity"/> about the world's up axis.
/// Self-registers with <see cref="OperationsMenu"/>.</summary>
[Subsystem(nameof(OperationsMenu))]
public sealed class RandomizeRotation : IOperation
{
    private readonly SelectionSystem _selection;
    private readonly EditSessionManager _sessions;
    private readonly ShortcutAction _shortcut;

    public string Name => "Randomize Rotation";

    public string ShortcutLabel => _shortcut.ShortcutLabel;

    public RandomizeRotation(OperationsMenu menu)
    {
        _selection = menu.Context.Selection;
        _sessions = menu.Context.EditSessions;
        _shortcut = menu.Context.Shortcuts.Register(
            "operations.randomize-rotation",
            "Operations",
            "Randomize Rotation",
            KeyboardShortcut.None,
            Apply,
            CanApply);
    }

    public bool CanApply() => Targets().Any();

    // Only yaws about the world's up axis: even Full-rotation entities keep their pitch/roll, since
    // "along the up axis" is the only thing this operation is meant to randomize.
    public void Apply()
    {
        SceneEntity[] entities = Targets().ToArray();
        if (entities.Length == 0)
        {
            return;
        }

        var before = new Transform3D[entities.Length];
        var after = new Transform3D[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            Transform3D transform = entities[i].Transform;
            before[i] = transform;
            Basis rotation = new Basis(Vector3.Up, GD.Randf() * Mathf.Tau);
            after[i] = new Transform3D(rotation.ScaledLocal(transform.Basis.Scale), transform.Origin);
            entities[i].Transform = after[i];
        }

        _sessions.Record(new TransformEntitiesCommand(entities, before, after));
    }

    // Excludes entities whose SelfRotation is None, matching what the object tool itself honours.
    private IEnumerable<SceneEntity> Targets() =>
        _selection.Selected.OfType<SceneEntity>().Where(entity => entity.SelfRotation != SelfRotation.None);
}
