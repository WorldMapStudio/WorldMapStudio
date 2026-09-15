namespace WorldMapStudio;

/// <summary>Colors <see cref="TransformGizmo"/> draws its handles with.</summary>
[StyleTokens]
public static class GizmoColors
{
    public static readonly StyleColor AxisX = new("gizmo.axis.x", "Gizmo", "Axis X", "#E83D47");
    public static readonly StyleColor AxisY = new("gizmo.axis.y", "Gizmo", "Axis Y", "#7DC729");
    public static readonly StyleColor AxisZ = new("gizmo.axis.z", "Gizmo", "Axis Z", "#387DED");
    public static readonly StyleColor Highlight = new("gizmo.highlight", "Gizmo", "Highlight", "#FFC91C");
    public static readonly StyleColor Pivot = new("gizmo.pivot", "Gizmo", "Pivot", "#EBEBEB");
    public static readonly StyleColor Outline = new("gizmo.outline", "Gizmo", "Outline", "#0D0D0DE6");
    public static readonly StyleColor AngleLabel = new("gizmo.angle-label", "Gizmo", "Angle Label", "#FFFFFF");
}
