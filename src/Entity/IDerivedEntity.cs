namespace WorldMapStudio;

/// <summary>
/// An entity the editor <em>computes</em> rather than stores — a landscape chunk being the first.
/// Derived entities live in the scene like any other, so they render, appear in the outline and get
/// an inspector, but they are never persisted and never edited directly: their content is a function
/// of the entities that produced them, and the way to change one is to change those.
///
/// The edit session refuses to pin these and the database refuses to stage them. That is a typed
/// rule rather than a convention on purpose — the first tool that tries to edit a chunk in place
/// should fail loudly instead of quietly writing a computed result into the database.
/// </summary>
public interface IDerivedEntity : IEntity
{
}
