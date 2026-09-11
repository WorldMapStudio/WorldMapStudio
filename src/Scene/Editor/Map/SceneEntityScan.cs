using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// What one factory's scan found. <see cref="Built"/> holds only the entities the factory had to
/// construct; <see cref="Keys"/> holds every persistent key the scan matched, including the ones it
/// did not build because streaming already had them. <see cref="Catalog"/> holds every catalog entity
/// (e.g. a <see cref="ProceduralModel"/>) a component persister resolved while building those entities —
/// see <see cref="SceneEntityScanCatalog"/>.
///
/// The Built/Keys split exists because those two answers have very different costs and streaming needs
/// both. Building an entity means reading its row and every component table it owns; knowing a key was
/// in region is one projected column. Streaming judges what stays resident from the second, so a scan
/// that rebuilds nothing still says everything it needs to.
/// </summary>
public readonly record struct SceneEntityScan(IReadOnlyList<SceneEntity> Built, IReadOnlyList<long> Keys, IReadOnlyList<CatalogEntity> Catalog)
{
    public static SceneEntityScan Empty { get; } = new([], [], []);
}
