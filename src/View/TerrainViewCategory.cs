namespace WorldMapStudio;

/// <summary>
/// One toggle over every built terrain batch. Core, since terrain is a core concept, and defined
/// over <see cref="LandscapeTerrainBatch"/> — the render unit, not the authoring landscape data — so
/// hiding it never touches terrain building, export, or streaming.
/// </summary>
[Subsystem(nameof(ViewCategorySystem))]
public sealed class TerrainViewCategory : IViewCategory
{
    public const string CategoryId = "view.terrain";

    public string Id => CategoryId;

    public string DisplayName => "Terrain";

    public string Group => "Scene";

    public TerrainViewCategory(ViewCategorySystem system)
    {
    }

    public bool Includes(SceneEntity entity) => entity is LandscapeTerrainBatch;
}
