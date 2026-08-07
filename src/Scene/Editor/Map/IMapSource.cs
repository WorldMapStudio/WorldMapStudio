using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Supplies the maps of one storage. Self-registers into a concrete storage with
/// [Subsystem(nameof(ThatStorage))], the same way <see cref="ISceneEntityFactory"/> does, so a plugin
/// can expose its own maps (e.g. a game client's map table) beside the built-in ones. A read-only
/// source reports <see cref="CanEdit"/> false and the editor won't offer to change its maps.
/// </summary>
public interface IMapSource : ISubsystem
{
    /// <summary>Every map this source knows about.</summary>
    Task<IReadOnlyList<Map>> LoadAsync();

    /// <summary>Whether the user may add, rename and remove this source's maps.</summary>
    bool CanEdit { get; }

    /// <summary>Persists a map the user just created. Only called when <see cref="CanEdit"/>.</summary>
    Task CreateAsync(Map map);

    /// <summary>Persists the already-updated <see cref="Map.Name"/>. Only called when <see cref="CanEdit"/>.</summary>
    Task RenameAsync(Map map);

    /// <summary>
    /// Removes the map itself. Entities placed in it are <em>not</em> touched — a source owns its map
    /// rows, not the (possibly many, possibly foreign) tables that reference them by id.
    /// </summary>
    Task DeleteAsync(Map map);
}
