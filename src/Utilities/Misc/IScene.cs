#nullable enable
namespace WorldMapStudio;

/// <summary>
/// A top-level application screen (main menu, project select, editor, ...). The host node owns
/// exactly one active scene, calls <see cref="Start"/> once when it becomes active, then calls
/// <see cref="Update"/> every frame. <see cref="Update"/> returns the scene to run next frame:
/// itself to stay, a new scene to transition, or <c>null</c> to quit the application.
/// </summary>
public interface IScene
{
    public void Start();
    public IScene? Update();
}
