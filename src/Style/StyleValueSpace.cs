namespace WorldMapStudio;

/// <summary>Which sparse dictionary on a <see cref="StyleDocument"/> a color id lives in — shared by
/// the Style Editor's generic color row and <c>wms.style</c>'s id grammar.</summary>
public enum StyleValueSpace
{
    Palette,
    ImGuiColor,
    Token,
}
