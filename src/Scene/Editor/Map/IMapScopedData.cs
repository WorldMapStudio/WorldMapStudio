using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// One owner's share of a map's data: a table (or set of tables) keyed on a map, or reachable through
/// the map's entities. Self-registers with [Subsystem(nameof(EditorStorage))], the same way
/// <see cref="ITableConfiguration"/> owners do, so deleting a map's contents never has to name a table
/// by hand — see <see cref="EditorStorage.MapScopedData"/>.
/// </summary>
public interface IMapScopedData : ISubsystem
{
    /// <summary>Shown in the delete popup, e.g. "Scene entities", "Landscape catalog".</summary>
    string Label { get; }

    Task<int> CountAsync(EditorDbContext context, MapId map);

    /// <summary>Every map id this owner holds rows for, including one with no <c>wms_maps</c> row —
    /// what <see cref="MapSystem.FindStrayMapIdsAsync"/> unions across every owner.</summary>
    Task<IReadOnlySet<int>> MapIdsAsync(EditorDbContext context);

    /// <summary>
    /// Deletes this owner's rows for <paramref name="map"/>. Runs inside the delete transaction, with
    /// <paramref name="context"/>'s connection already open. <see cref="ISubsystem.Priority"/> orders
    /// owners, lower first: an owner that finds its rows through the map's entities (rather than a
    /// column naming the map directly) needs a negative priority so it runs before
    /// <c>SceneEntityFactory</c> (priority 0) deletes them.
    /// </summary>
    Task DeleteAsync(EditorDbContext context, DbTransaction transaction, MapId map);
}
