namespace WorldMapStudio;

/// <summary>The handful of semantic colors used all over the core editor, for error/warning/success/accent call
/// sites.</summary>
[StyleTokens]
public static class CommonColors
{
    public static readonly StyleColor Error = new("text.error", "Text", "Error", "#FF7366");
    public static readonly StyleColor Warning = new("text.warning", "Text", "Warning", "#FFB333");
    public static readonly StyleColor Success = new("text.success", "Text", "Success", "#6BD975");
    public static readonly StyleColor Accent = new("text.accent", "Text", "Accent", "#73BFFF");
}
