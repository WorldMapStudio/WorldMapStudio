namespace WorldMapStudio;

/// <summary>A window that can show an entity it didn't come from. Found on
/// <see cref="WindowManager"/>; callers never name the window.</summary>
public interface IEntityOpener
{
    /// <summary>The button text, e.g. "Open in Dress Up".</summary>
    string OpenLabel { get; }

    bool CanOpen(IEntity entity);

    void Open(IEntity entity);
}
