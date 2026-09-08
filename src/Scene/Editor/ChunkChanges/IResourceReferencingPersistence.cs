using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A scene-component persister whose rows reference a shared resource by id — a procedural mesh's
/// model, an image placement's image. Lets <see cref="ChunkChangeLog"/> find every placement of a
/// resource that was just edited, including the ones streamed out, so their chunks are restamped for
/// export.
///
/// Implemented alongside <see cref="ISceneComponentPersistence"/> on the same class, so it is reached
/// through <see cref="EditorStorage.ComponentPersistence"/> with no extra registration.
/// </summary>
public interface IResourceReferencingPersistence
{
    /// <summary>The resource type these rows reference, e.g. <c>typeof(ProceduralModel)</c>.</summary>
    Type ReferencedResourceType { get; }

    /// <summary>
    /// Every stored scene entity whose component of this kind references
    /// <paramref name="resourceRecordId"/>: its row id, map, and last-committed
    /// <see cref="SceneEntity.WorldBounds"/>. The bounds are as last saved — not grown for a resource
    /// edit that enlarged the geometry, since the entity is not loaded to re-measure.
    /// </summary>
    Task<IReadOnlyList<(int EntityId, MapId Map, Aabb Bounds)>> ReferencingBoundsAsync(
        EditorDbContext context,
        int resourceRecordId);
}
