using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The immutable result of resolving one style's <c>extends</c> chain: every <c>ImGuiCol</c> and
/// numeric <c>ImGuiStyle</c> field fully populated (floor merged with overrides), plus the resolved
/// palette and semantic tokens. Never partial — a broken chain still produces a complete style, with
/// the breakage recorded in <see cref="Problems"/> instead.
/// </summary>
public sealed class ResolvedStyle
{
    public required string Name { get; init; }
    public required IReadOnlyDictionary<string, Vector4> Palette { get; init; }
    public required IReadOnlyDictionary<ImGuiCol, Vector4> Colors { get; init; }
    public required IReadOnlyDictionary<string, Vector4> Tokens { get; init; }
    public required IReadOnlyDictionary<string, float> Sizes { get; init; }
    public required IReadOnlyDictionary<string, StyleVarValue> Vars { get; init; }
    public required float FontScale { get; init; }
    public required IReadOnlyDictionary<string, StyleFontSlotValue> FontSlots { get; init; }
    public required IReadOnlyList<StyleProblem> Problems { get; init; }

    /// <summary>Writes every resolved color and numeric field into <paramref name="style"/>. Doesn't
    /// touch fonts — see <c>FontAtlasBuilder</c> for that half of applying a style.</summary>
    public void Apply(ImGuiStylePtr style)
    {
        foreach ((ImGuiCol col, Vector4 color) in Colors)
        {
            style.Colors[(int)col] = color;
        }

        foreach (StyleVarFields.Field field in StyleVarFields.All)
        {
            field.Set(style, Vars[field.Id]);
        }
    }
}
