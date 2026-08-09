using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Adds a catalog entity to the registry (and removes it on undo). The catalog counterpart of
/// <see cref="CreateEntityCommand"/>: registry membership is what a commit reads to tell a save from
/// a delete, so undoing a creation is exactly what makes it never reach the database.
/// </summary>
public sealed class CreateCatalogEntityCommand(CatalogEntityRegistry catalog, CatalogEntity entity) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public string Description => $"Create {entity.DisplayName}";

    public void Apply() => catalog.Add(entity);

    public void Revert() => catalog.Remove(entity);
}

/// <summary>Removes a catalog entity from the registry (and restores it on undo). Deleted on commit.</summary>
public sealed class DeleteCatalogEntityCommand(CatalogEntityRegistry catalog, CatalogEntity entity) : IEditCommand
{
    public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

    public string Description => $"Delete {entity.DisplayName}";

    public void Apply() => catalog.Remove(entity);

    public void Revert() => catalog.Add(entity);
}
