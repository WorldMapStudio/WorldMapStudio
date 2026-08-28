namespace WorldMapStudio;

/// <summary>
/// Self-registers with [Subsystem(nameof(EditorContext))] to load its own catalog once the migration
/// gate has passed, exactly like the built-in landscape catalog does (see
/// <see cref="LandscapeSystem.Load"/>) — for a plugin that owns a catalog entity type the core does
/// not know about and so has nowhere else to hook a one-time "read this catalog whole" step into.
/// </summary>
public interface ICatalogAutoLoader : ISubsystem
{
    void Load();
}
