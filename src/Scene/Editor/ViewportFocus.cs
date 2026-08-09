using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Lets any window ask the viewport to look at a place in the world, or read/move the camera
/// directly, without depending on the viewport itself. The viewport installs the handlers; everything
/// else just asks.
///
/// Same shape as <see cref="MapSystem.CaptureView"/>: only the viewport can do it, but plenty of
/// things need it done.
/// </summary>
public sealed class ViewportFocus
{
    /// <summary>Set by the viewport. Null until it exists, in which case requests are ignored.</summary>
    public Action<Vector3>? Handler { get; set; }

    public Func<Vector3>? PositionGetter { get; set; }

    public Action<Vector3>? PositionSetter { get; set; }

    public void LookAt(Vector3 world) => Handler?.Invoke(world);

    /// <summary>The camera's current world position, or the origin if the viewport doesn't exist yet.</summary>
    public Vector3 Position => PositionGetter?.Invoke() ?? Vector3.Zero;

    /// <summary>Teleports the camera, keeping its current orientation. No-op if the viewport doesn't exist yet.</summary>
    public void MoveTo(Vector3 world) => PositionSetter?.Invoke(world);
}
