using Godot;

namespace WorldMapStudio;

/// <summary>
/// The node inside the 3D viewport's own world, for anything drawn there that is not a scene entity —
/// overlays, gizmos, previews of derived data. The viewport renders an isolated world, so a node parented
/// anywhere else, <see cref="EditorContext.Root"/> included, never appears in it.
///
/// Same shape as <see cref="ViewportFocus"/>: only the viewport can supply it, so it is set by the
/// viewport and null until one exists.
/// </summary>
public sealed class ViewportSurface
{
    public Node? Node { get; set; }
}
