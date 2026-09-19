using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A scene-entity factory creatable by script and UI through one shared <see cref="Create"/> — the
/// scene-entity sibling of <see cref="ICatalogBrowser"/>. Implemented on the same class as
/// <see cref="ISceneEntityFactory"/> for that entity kind.
///
/// <see cref="SpawnScriptApi"/> exposes every registered factory to scripts (<c>wms.spawn.Create</c>),
/// and the Scene-menu "place under the pointer" action goes through the same <see cref="Create"/>, so
/// the two paths cannot drift.
/// </summary>
public interface ISpawnFactory : IEntityFactory
{
    /// <summary>Display/script name for this kind of spawn, e.g. "Creature" — what
    /// <see cref="SpawnScriptApi.List"/> reports and <c>wms.spawn.Create</c> names it by.</summary>
    string SpawnKind { get; }

    /// <summary>The <see cref="ICatalogBrowser.CatalogName"/> of the catalog whose keys
    /// <see cref="Create"/> accepts, so a placement UI can open the right <see cref="CatalogEntityPicker"/>.
    /// Empty means this kind has no catalog of origin (a taxi node, an area trigger);
    /// <see cref="SpawnMenu"/> then creates directly with no key.</summary>
    string PickerCatalogName { get; }

    /// <summary>
    /// Creates a new entity of this kind at <paramref name="transform"/> on <paramref name="map"/>, with
    /// the game-data identity <paramref name="key"/> names (a string like <see cref="ICatalogBrowser.Create"/>'s,
    /// e.g. a creature template entry). Adds it through a recorded <c>CreateEntityCommand</c>, so creation
    /// is undoable and persisted on commit.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is malformed or doesn't exist.</exception>
    SceneEntity Create(EditorContext context, MapId map, Transform3D transform, string key);
}
