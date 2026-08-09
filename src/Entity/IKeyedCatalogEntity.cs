namespace WorldMapStudio;

/// <summary>
/// A catalog entity identified by a row id that other entities store to reference it.
///
/// The id is assigned by the editor when the entity is created, <b>not</b> by the database. Letting
/// the database generate it meant nothing could reference a catalog entity until the session was
/// committed — a stamp created alongside a new layer silently bound to nothing, and pickers had no
/// id to offer. The <c>maps</c> table already works this way, with a non-generated primary key.
///
/// That splits two things the database used to conflate: <see cref="RecordId"/> is <em>who this is</em>,
/// and <see cref="IsSaved"/> is <em>whether a row exists yet</em>. A factory needs the second to know
/// whether to insert or update.
/// </summary>
public interface IKeyedCatalogEntity
{
    /// <summary>Row id, assigned at creation so references work before anything is saved.</summary>
    int? RecordId { get; set; }

    /// <summary>Whether a row already exists for this entity in its storage.</summary>
    bool IsSaved { get; set; }
}
