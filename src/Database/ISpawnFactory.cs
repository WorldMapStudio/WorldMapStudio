using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Makes a scene-entity factory creatable by script and UI through one shared method — the scene
/// entity sibling of <see cref="ICatalogBrowser"/>. A class implementing this already implements
/// <see cref="ISceneEntityFactory"/> for the same entity kind; this is a second, independent
/// capability on that same class, not a replacement — exactly the way <see cref="ICatalogBrowser"/>
/// sits alongside <see cref="ICatalogEntityFactory"/>/<see cref="ILazyCatalogEntityFactory"/>.
///
/// <see cref="SpawnScriptApi"/> is the one generic script module for every registered factory: a
/// scene-entity kind becomes scriptably creatable purely by implementing this, the same way a catalog
/// becomes browsable purely by implementing <see cref="ICatalogBrowser"/>. A Scene-menu "place under
/// the pointer" UI action and a script's <c>wms.spawn.Create</c> call are both meant to go through
/// this one <see cref="Create"/>, so the two paths cannot drift the way a bespoke UI-only creation
/// method and a bespoke script-only one could.
/// </summary>
public interface ISpawnFactory : IEntityFactory
{
    /// <summary>Display/script name for this kind of spawn, e.g. "Creature" — what
    /// <see cref="SpawnScriptApi.List"/> reports and <c>wms.spawn.Create</c> names it by.</summary>
    string SpawnKind { get; }

    /// <summary>The <see cref="ICatalogBrowser.CatalogName"/> of the catalog whose keys this factory's
    /// <see cref="Create"/> accepts. Lets a generic interactive placement UI (a Scene-menu "place under
    /// the pointer" action) open the right <see cref="CatalogEntityPicker"/> for any registered factory
    /// without naming a spawn kind by hand, the same way this whole interface lets
    /// <c>wms.spawn.Create</c> dispatch by <see cref="SpawnKind"/> alone.
    ///
    /// The empty string documents "this kind has no catalog of origin" — it instances nothing, the way
    /// a taxi node or an area trigger doesn't. <see cref="SpawnMenu"/> creates directly with no key in
    /// that case instead of trying to open a picker.</summary>
    string PickerCatalogName { get; }

    /// <summary>
    /// Creates a new entity of this kind at <paramref name="transform"/> on <paramref name="map"/>,
    /// with the game-data identity <paramref name="key"/> names — a creature template entry as a
    /// string, for a creature spawn factory; whatever a factory's own catalog-of-origin identifies a
    /// row by, mirroring <see cref="ICatalogBrowser.Create"/>'s uniform string key even though the real
    /// key type differs per spawn kind. Adds the entity to the scene through a recorded
    /// <c>CreateEntityCommand</c> so creation is undoable and persisted on commit, exactly like
    /// <see cref="ICatalogBrowser.Create"/> is for catalog entities.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is malformed or doesn't exist. Loud on
    /// purpose, matching <see cref="ICatalogBrowser.Create"/>.</exception>
    SceneEntity Create(EditorContext context, MapId map, Transform3D transform, string key);
}
