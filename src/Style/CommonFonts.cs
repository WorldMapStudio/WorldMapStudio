namespace WorldMapStudio;

/// <summary>The two core-owned font slots. "Ui" is the atlas's first font — ImGui's default for
/// everything — so nothing pushes it explicitly; "Monospace" is pushed by the script console and the
/// log window.</summary>
[StyleTokens]
public static class CommonFonts
{
    public static readonly StyleFont Ui = new("ui", "Interface", defaultSize: 13);
    public static readonly StyleFont Monospace = new("monospace", "Monospace", defaultSize: 13);
}
