using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Lets any window ask the viewport to look at a place in the world, without depending on the
/// viewport itself. The viewport installs <see cref="Handler"/>; everything else just asks.
///
/// Same shape as <see cref="MapSystem.CaptureView"/>: only the viewport can do it, but plenty of
/// things need it done.
/// </summary>
public sealed class ViewportFocus
{
    /// <summary>Set by the viewport. Null until it exists, in which case requests are ignored.</summary>
    public Action<Vector3>? Handler { get; set; }

    public void LookAt(Vector3 world) => Handler?.Invoke(world);
}
