using Godot;

namespace WorldMapStudio;

public abstract class SceneComponent
{
    public SceneEntity? Owner { get; internal set; }

    public abstract string TypeId { get; }

    public abstract string DisplayName { get; }

    public virtual int ContentVersion => 0;

    protected SceneEntity Entity => Owner ?? throw new System.InvalidOperationException("Component is not attached to an entity.");

    /// <summary>Creates an unattached, independent copy of this component's data (no <see cref="Owner"/>).
    /// Used to duplicate a scene entity, e.g. for copy/paste, without the copy sharing any mutable state
    /// (buffers, graphs) with the original.</summary>
    public abstract SceneComponent Clone();
}

public interface ISceneBoundsProvider
{
    Aabb LocalBounds { get; }

    /// <summary>
    /// Whether <see cref="LocalBounds"/> means "the whole map" rather than a place on it — a global
    /// light being the case this exists for. Such a component needs real, enormous bounds so streaming
    /// keeps it loaded wherever the viewport goes, but those bounds say nothing about <em>where</em>
    /// anything is, so they must never be what puts a chunk on the map. See
    /// <see cref="SceneEntity.LocalChunkBounds"/>, which is what chunk ownership is decided from.
    /// </summary>
    bool IsMapSpanning => false;
}

public interface ISceneNodeComponent
{
    Node3D? BuildNode();
}

/// <summary>
/// Marks an <see cref="ISceneNodeComponent"/> whose built node is real, clickable geometry (a
/// model, a procedural mesh) rather than an editor helper (a marker gizmo, a paint-area stamp).
/// Click selection ray-tests these components' <see cref="MeshInstance3D"/> triangles directly
/// instead of falling back to the entity's bounding box; see <see cref="SceneEntity.TryPickGeometry"/>.
/// </summary>
public interface IMeshPickable
{
}

public interface ITransformPolicy
{
    SelfRotation SelfRotation { get; }

    SelfScale SelfScale { get; }
}
