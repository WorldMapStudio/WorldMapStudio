using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Draws screen-space content over the viewport's 3D scene image — glyphs, labels, anything that
/// isn't a 3D-space gizmo drawn into the scene itself. Self-registers with
/// <c>[Subsystem(nameof(ViewportWindow))]</c>, the same shape any other per-frame viewport
/// contributor (a tool, a gizmo) already follows elsewhere in this window.
///
/// Runs every frame regardless of the active tool, right after <see cref="ViewportWindow"/> draws the
/// scene image and before it hands the frame to that tool's own <c>UpdateViewport</c> — so a tool's
/// own gizmos still draw on top.
/// </summary>
public interface IViewportOverlay : ISubsystem
{
    /// <summary><paramref name="projector"/> and <paramref name="camera"/> both describe this frame's
    /// camera — the projector for turning world points into pixels on <paramref name="drawList"/>
    /// cheaply, the camera itself for anything a projection alone doesn't answer (e.g. distance-based
    /// culling against <see cref="Node3D.GlobalPosition"/>).</summary>
    void Draw(ViewportProjector projector, ImDrawListPtr drawList, Camera3D camera);
}
