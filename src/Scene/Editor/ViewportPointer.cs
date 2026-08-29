using Godot;

namespace WorldMapStudio;

/// <summary>
/// Where the mouse currently projects into the world, and whether it's over the viewport at all — set
/// by the viewport every frame it draws, read by anything that places something under the cursor
/// (e.g. paste). Shortcut/menu processing runs before the viewport draws for the frame, so this is one
/// frame behind; imperceptible at frame rate.
/// </summary>
public sealed class ViewportPointer
{
    /// <summary>Whether the mouse is over the viewport image at all.</summary>
    public bool Hovered { get; set; }

    /// <summary>Whether <see cref="WorldPoint"/> is meaningful this frame (a ray was actually cast and
    /// hit something — terrain or the fallback ground plane).</summary>
    public bool Valid { get; set; }

    /// <summary>Where the mouse ray hits the terrain (or the Y=0 ground plane with none loaded).</summary>
    public Vector3 WorldPoint { get; set; }
}
