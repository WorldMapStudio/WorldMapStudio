#nullable enable
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Dispatched once, right after the ImGui layer is up and before <see cref="WorldMapStudioApp"/>
/// picks a starting scene. A handler that calls <see cref="OpenScene"/> or <see cref="Quit"/> takes
/// over the launch entirely — the app runs that scene (or quits) instead of showing
/// <see cref="MainMenu"/>. A plugin can use this to configure and save its own <see cref="Project"/>
/// (see <see cref="ProjectStore"/>) and jump straight into <see cref="LoadingScreen.OpenProject"/>,
/// skipping the project picker altogether. Left unhandled, startup proceeds exactly as if the event
/// API weren't there.
/// </summary>
public sealed class StartupEvent : Event
{
    /// <summary>The app's root node, for handlers that need it to build a scene (e.g.
    /// <see cref="LoadingScreen.OpenProject"/>).</summary>
    public required Node3D Root { get; init; }

    public IScene? Scene { get; private set; }

    public bool QuitRequested { get; private set; }

    /// <summary>Starts the app on <paramref name="scene"/> instead of the main menu.</summary>
    public void OpenScene(IScene scene)
    {
        Scene = scene;
        Consume();
    }

    /// <summary>Quits the app before any scene ever runs.</summary>
    public void Quit()
    {
        QuitRequested = true;
        Consume();
    }
}
