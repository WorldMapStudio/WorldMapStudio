using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// A named font slot — "ui" (the atlas's first font, ImGui's default for everything), "monospace",
/// or a plugin-declared slot — pushed where wanted via <c>ImGuiEx.PushFont</c>. Self-registers with
/// <see cref="StyleTokenRegistry"/> on construction, the same way <see cref="StyleColor"/> does.
/// </summary>
public sealed class StyleFont
{
    public string Id { get; }
    public string DefaultFamily { get; }
    public float DefaultSize { get; }

    /// <summary>Rebound by <c>FontAtlasBuilder</c> after every atlas rebuild — call sites never cache
    /// this themselves, they read it fresh through <c>ImGuiEx.PushFont</c> each frame.</summary>
    public ImFontPtr Pointer { get; internal set; }

    public StyleFont(string id, string defaultFamily, float defaultSize)
    {
        Id = id;
        DefaultFamily = defaultFamily;
        DefaultSize = defaultSize;
        StyleTokenRegistry.RegisterFont(this);
    }
}
