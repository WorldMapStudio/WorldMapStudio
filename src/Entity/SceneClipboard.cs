using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Holds an independent copy of the last-copied scene entities. Copying clones every entity and its
/// components immediately, so paste never depends on the originals still being around — streaming can
/// unload the source, or an undo can delete it, between copy and paste, and the clipboard is
/// unaffected because it already owns its own data. A plain member of <see cref="EditorContext"/>,
/// shared by every tool: whichever tool the selection was made in, copy/paste behaves the same.
/// </summary>
public sealed class SceneClipboard : IWorldParticipant
{
    private IReadOnlyList<SceneEntity> _entries = [];

    public bool HasContent => _entries.Count > 0;

    /// <summary>Clones each entity, preserving parent links between entities that were copied together
    /// and dropping any link to a parent that wasn't.</summary>
    public void Copy(IEnumerable<SceneEntity> entities) => _entries = CloneBatch(entities.ToArray(), map: null);

    /// <summary>
    /// Produces fresh, independently-identified copies ready to add to the scene in <paramref name="map"/>.
    /// Safe to call repeatedly: each call re-clones the stored snapshot, so pasting more than once never
    /// shares an entity or component instance between the results.
    /// </summary>
    public IReadOnlyList<SceneEntity> Paste(MapId map) => CloneBatch(_entries, map);

    /// <summary>
    /// <see cref="Paste"/>, shifted rigidly so the bottom-centre of the batch's combined bounds lands at
    /// <paramref name="point"/>, preserving whatever layout (and, for a parent/child pair, relative
    /// offset) the copied entities had. Returns the entities and the not yet applied command that adds
    /// them to <paramref name="scene"/>, or a null command when there is nothing to paste.
    /// </summary>
    public (IReadOnlyList<SceneEntity> Entities, IEditCommand? Command) PasteAt(SceneEntityRegistry scene, MapId map, Vector3 point)
    {
        IReadOnlyList<SceneEntity> pasted = Paste(map);
        if (pasted.Count == 0)
        {
            return (pasted, null);
        }

        // Moving a parent carries its children along, so only the ones without a copied parent are moved.
        Vector3 delta = point - BoundsBottomCenter(pasted);
        foreach (SceneEntity entity in pasted.Where(entity => entity.Parent == null))
        {
            Transform3D transform = entity.Transform;
            entity.Transform = new Transform3D(transform.Basis, transform.Origin + delta);
        }

        List<IEditCommand> commands = pasted.Select(entity => (IEditCommand)new CreateEntityCommand(scene, entity)).ToList();
        IEditCommand command = commands.Count == 1
            ? commands[0]
            : new BatchEditCommand($"Paste {commands.Count} entities", commands);
        return (pasted, command);
    }

    /// <summary>Drops the copied entities, e.g. across a world reload — they may reference components
    /// (a bound image, a procedural model) that no longer resolve once the catalogs reload.</summary>
    public void Clear() => _entries = [];

    void IWorldParticipant.UnloadWorld() => Clear();

    // Horizontally centred, vertically at the lowest point: the natural anchor for dropping a batch
    // onto a surface, so pasted content sits on the ground under the cursor rather than being buried
    // or floating.
    private static Vector3 BoundsBottomCenter(IReadOnlyList<SceneEntity> entities)
    {
        Aabb bounds = entities[0].WorldBounds;
        for (int i = 1; i < entities.Count; i++)
        {
            bounds = bounds.Merge(entities[i].WorldBounds);
        }

        return new Vector3(bounds.Position.X + bounds.Size.X * 0.5f, bounds.Position.Y, bounds.Position.Z + bounds.Size.Z * 0.5f);
    }

    private static IReadOnlyList<SceneEntity> CloneBatch(IReadOnlyList<SceneEntity> source, MapId? map)
    {
        var clones = new Dictionary<SceneEntity, SceneEntity>();
        foreach (SceneEntity entity in source)
        {
            SceneEntity clone = entity.Clone();
            if (map is { } target)
            {
                clone.Map = target;
            }

            clones[entity] = clone;
        }

        foreach (SceneEntity entity in source)
        {
            if (entity.Parent is { } parent && clones.TryGetValue(parent, out SceneEntity? parentClone))
            {
                clones[entity].Parent = parentClone;
            }
        }

        return clones.Values.ToArray();
    }
}
