using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Declares a component kind to the editor: how to create one and how to draw its inspector.
/// Register with [Subsystem(nameof(SceneComponentRegistry))] so the inspector's "Add" combo and
/// per-component body are driven by the registry instead of a hardcoded switch, letting a plugin
/// contribute its own component kinds the same way the built-in ones are declared.
/// </summary>
public interface ISceneComponentType : ISubsystem
{
    /// <summary>Matches the created component's <see cref="SceneComponent.TypeId"/>.</summary>
    string TypeId { get; }

    string DisplayName { get; }

    /// <summary>Whether this kind may be attached to an entity stored outside the editor's own tables.
    /// False for anything whose persistence or evaluation is scoped to an editor map's entities.</summary>
    bool AttachesToBridgedEntities => false;

    /// <summary>Whether the entity may receive another of this kind. Default: at most one, and only on an
    /// editor-authored entity unless <see cref="AttachesToBridgedEntities"/> says otherwise.</summary>
    bool CanAddTo(SceneEntity entity) =>
        (entity is MapSceneEntity || AttachesToBridgedEntities)
        && entity.Components.All(component => component.TypeId != TypeId);

    SceneComponent Create();

    /// <summary>Draws the component's fields, recording edits into <see cref="InspectorContext.Sessions"/>.</summary>
    void DrawInspector(InspectorContext context, SceneComponent component);

    /// <summary>
    /// Draws anything that must run every frame regardless of the current selection, such as an
    /// asset-picker modal opened from <see cref="DrawInspector"/>. Most types need nothing here.
    /// </summary>
    void DrawModals() { }
}
