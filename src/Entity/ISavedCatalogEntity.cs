namespace WorldMapStudio;

/// <summary>
/// A catalog entity that needs to know whether a row for it exists in storage yet, to choose insert
/// vs. update and to skip deleting a row that was never saved.
///
/// This is the half of <see cref="IKeyedCatalogEntity"/> that doesn't depend on the editor allocating
/// the key: a catalog keyed by a value that already means something in the game's own data (a fixed
/// type id, an entry, a params id) can implement this without a <c>RecordId</c> to allocate. Use
/// <see cref="SavedEntityStaging"/> for the shared insert/update/delete branching that goes with it.
/// </summary>
public interface ISavedCatalogEntity
{
    /// <summary>Whether a row already exists for this entity in its storage.</summary>
    bool IsSaved { get; set; }
}
