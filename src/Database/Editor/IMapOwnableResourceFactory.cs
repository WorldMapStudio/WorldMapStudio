using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// A catalog whose rows can be deleted along with the map that was their only user — a paint image, a
/// procedural model. Self-registers with [Subsystem(nameof(EditorStorage))] on the same class that
/// already implements <see cref="ICatalogEntityFactory"/> or <see cref="ILazyCatalogEntityFactory"/> for
/// the resource. Reached through <see cref="EditorStorage.MapOwnableResourceFactories"/>; each
/// registered implementer becomes one checkbox in the map delete popup, so a new resource kind shows up
/// there with no change to the popup itself. See <see cref="EditorStorage.FindMapOnlyResourcesAsync"/>
/// for which ids actually go.
/// </summary>
public interface IMapOwnableResourceFactory
{
    /// <summary>The resource type this owns, e.g. <c>typeof(PaintImage)</c> — matches some
    /// <see cref="IResourceReferencingPersistence.ReferencedResourceType"/>.</summary>
    Type ResourceType { get; }

    /// <summary>Shown as the checkbox label in the delete popup, e.g. "Images", "Procedural models".</summary>
    string Label { get; }

    /// <summary>Deletes the rows for <paramref name="ids"/>. Runs inside the delete transaction, with
    /// <paramref name="context"/>'s connection already open.</summary>
    Task DeleteAsync(EditorDbContext context, DbTransaction transaction, IReadOnlyCollection<int> ids);
}
