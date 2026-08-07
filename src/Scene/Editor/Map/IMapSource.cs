using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Supplies the maps of one storage. Self-registers into a concrete storage with
/// [Subsystem(nameof(ThatStorage))], the same way <see cref="ISceneEntityFactory"/> does, so a plugin
/// can expose its own maps (e.g. a game client's map table) beside the built-in ones. A read-only
/// source reports <see cref="CanCreate"/> false and the editor simply won't offer to add maps to it.
/// </summary>
public interface IMapSource : ISubsystem
{
    /// <summary>Every map this source knows about.</summary>
    Task<IReadOnlyList<Map>> LoadAsync();

    /// <summary>Whether the user may add new maps to this source.</summary>
    bool CanCreate { get; }

    /// <summary>Persists a map the user just created. Only called when <see cref="CanCreate"/>.</summary>
    Task CreateAsync(Map map);
}
