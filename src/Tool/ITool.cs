namespace WorldMapStudio;

/// <summary>An editor mode that owns viewport interaction while active, like Blender's modes.</summary>
public interface ITool
{
    string Name { get; }

    /// <summary>True while the tool owns the mouse (gizmo/marquee/modal), so the camera yields.</summary>
    bool CapturesMouse { get; }

    /// <summary>Draws the tool's own controls into the viewport toolbar.</summary>
    void DrawToolbar();

    /// <summary>Runs one frame of viewport interaction.</summary>
    void UpdateViewport(in ViewportContext context);

    void Activate() { }

    void Deactivate() { }
}
