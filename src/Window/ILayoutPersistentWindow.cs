using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>
/// A <see cref="Window"/> with view state worth keeping across sessions beyond its open/closed flag
/// and ImGui's own saved geometry — a preview camera, a splitter position. The layout profile stores
/// whatever <see cref="CaptureLayoutState"/> returns under the window's title and returns it to
/// <see cref="RestoreLayoutState"/> when that profile is next loaded.
/// </summary>
public interface ILayoutPersistentWindow
{
    JsonObject? CaptureLayoutState();

    void RestoreLayoutState(JsonObject state);
}
