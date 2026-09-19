namespace WorldMapStudio;

[Subsystem(nameof(ViewMenu))]
public sealed class EnvironmentVolumesMenuItem(ViewMenu menu) : ViewToggleMenuItem(
    menu, 4f, "view.environment-volumes", "Environment Volumes", KeyboardShortcut.None,
    view => view.ShowEnvironmentVolumes, (view, shown) => view.ShowEnvironmentVolumes = shown);
