using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Holds an independent copy of the last-copied scene entities. Copying clones every entity and its
/// components immediately, so paste never depends on the originals still being around — streaming can
/// unload the source, or an undo can delete it, between copy and paste, and the clipboard is
/// unaffected because it already owns its own data. A plain member of <see cref="EditorContext"/>,
/// shared by every tool: whichever tool the selection was made in, copy/paste behaves the same.
/// </summary>
public sealed class SceneClipboard
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
