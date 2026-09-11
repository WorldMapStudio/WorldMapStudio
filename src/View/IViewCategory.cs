namespace WorldMapStudio;

/// <summary>
/// A per-type viewport visibility filter entry (e.g. "M2", "Terrain", "Lighting"), hosted by
/// <see cref="ViewCategorySystem"/> via <c>[Subsystem(nameof(ViewCategorySystem))]</c>. A category
/// answers for its own membership, so nothing that draws or hosts categories — the menu, the
/// registry, scripting — ever names one, the same relationship <c>ISpawnFactory</c> has with
/// <c>SpawnMenu</c>.
/// </summary>
public interface IViewCategory : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Menu section this sits under ("Scene", "Models", "Lighting", "Paths").</summary>
    string Group { get; }

    KeyboardShortcut DefaultShortcut => KeyboardShortcut.None;

    bool Includes(SceneEntity entity);
}
