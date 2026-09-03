namespace WorldMapStudio;

/// <summary>
/// A catalog entity identified by a row id that other entities store to reference it.
///
/// The id is assigned by the editor when the entity is created, <b>not</b> by the database. Letting
/// the database generate it meant nothing could reference a catalog entity until the session was
/// committed — a stamp created alongside a new layer silently bound to nothing, and pickers had no
/// id to offer. The <c>maps</c> table already works this way, with a non-generated primary key.
///
/// <see cref="RecordId"/> is <em>who this is</em>; <see cref="ISavedCatalogEntity.IsSaved"/> (inherited)
/// is <em>whether a row exists yet</em>. A catalog keyed by a game-authored value instead of an
/// editor-allocated one has no <c>RecordId</c> to give, so it implements <see cref="ISavedCatalogEntity"/>
/// directly rather than this interface.
/// </summary>
public interface IKeyedCatalogEntity : ISavedCatalogEntity
{
    /// <summary>Row id, assigned at creation so references work before anything is saved.</summary>
    int? RecordId { get; set; }
}
