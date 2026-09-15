using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Turns a style document into a <see cref="ResolvedStyle"/>: walks <c>extends</c> to a built-in
/// root, merges palette/vars/colors/tokens root-first (child wins), then resolves every $/@
/// reference against a visited set. Never throws — a cycle, a missing parent, or a dangling
/// reference becomes a <see cref="StyleProblem"/> and the value falls back to its parent (or a
/// code-declared default for tokens).
/// </summary>
public static class StyleResolver
{
    private static readonly IReadOnlyDictionary<string, StyleColorValue> EmptyTokenDefaults =
        new Dictionary<string, StyleColorValue>();

    private static readonly IReadOnlyDictionary<string, float> EmptySizeDefaults =
        new Dictionary<string, float>();

    public static ResolvedStyle Resolve(
        StyleDocument document,
        Func<string, StyleDocument?> lookupParent,
        IReadOnlyDictionary<string, StyleColorValue>? tokenDefaults = null,
        IReadOnlyDictionary<string, float>? sizeDefaults = null)
    {
        tokenDefaults ??= EmptyTokenDefaults;
        sizeDefaults ??= EmptySizeDefaults;

        List<StyleProblem> problems = new();
        (IReadOnlyList<StyleDocument> chain, string rootName) = BuildChain(document, lookupParent, problems);
        BuiltInStyles.Preset rootPreset = BuiltInStyles.Find(rootName) ?? BuiltInStyles.Find("Dark")!;

        Dictionary<string, StyleColorValue> palette = new(StringComparer.Ordinal);
        Dictionary<string, StyleColorValue> colors = new(StringComparer.Ordinal);
        Dictionary<string, StyleColorValue> tokens = new(StringComparer.Ordinal);
        Dictionary<string, StyleVarValue> vars = new(StringComparer.Ordinal);
        Dictionary<string, float> sizes = new(StringComparer.Ordinal);
        float? fontScale = null;
        Dictionary<string, StyleFontSlotValue> fontSlots = new(StringComparer.Ordinal);

        foreach (StyleDocument doc in chain)
        {
            foreach ((string key, StyleColorValue value) in doc.Palette) palette[key] = value;
            foreach ((string key, StyleColorValue value) in doc.Colors) colors[key] = value;
            foreach ((string key, StyleColorValue value) in doc.Tokens) tokens[key] = value;
            foreach ((string key, StyleVarValue value) in doc.Vars) vars[key] = value;
            foreach ((string key, float value) in doc.Sizes) sizes[key] = value;
            if (doc.Fonts.Scale is { } scale) fontScale = scale;

            foreach ((string slotId, StyleFontSlotValue slot) in doc.Fonts.Slots)
            {
                fontSlots[slotId] = fontSlots.TryGetValue(slotId, out StyleFontSlotValue? existing)
                    ? MergeFontSlot(existing, slot)
                    : slot;
            }

            problems.AddRange(doc.Problems);
        }

        ColorGraph graph = new(palette, colors, tokens, rootPreset.Colors, tokenDefaults, problems);

        Dictionary<ImGuiCol, Vector4> resolvedColors = new();
        foreach (ImGuiCol col in Enum.GetValues<ImGuiCol>())
        {
            if (col == ImGuiCol.COUNT) continue;
            resolvedColors[col] = graph.ResolveImGuiColor(col, new HashSet<string>(StringComparer.Ordinal));
        }

        Dictionary<string, Vector4> resolvedPalette = new(StringComparer.Ordinal);
        foreach (string key in palette.Keys)
        {
            resolvedPalette[key] = graph.ResolvePalette(key, new HashSet<string>(StringComparer.Ordinal));
        }

        HashSet<string> tokenIds = new(tokens.Keys, StringComparer.Ordinal);
        tokenIds.UnionWith(tokenDefaults.Keys);
        Dictionary<string, Vector4> resolvedTokens = new(StringComparer.Ordinal);
        foreach (string id in tokenIds)
        {
            resolvedTokens[id] = graph.ResolveToken(id, new HashSet<string>(StringComparer.Ordinal));
        }

        HashSet<string> sizeIds = new(sizes.Keys, StringComparer.Ordinal);
        sizeIds.UnionWith(sizeDefaults.Keys);
        Dictionary<string, float> resolvedSizes = new(StringComparer.Ordinal);
        foreach (string id in sizeIds)
        {
            resolvedSizes[id] = sizes.TryGetValue(id, out float value) ? value : sizeDefaults[id];
        }

        Dictionary<string, StyleVarValue> resolvedVars = new(StringComparer.Ordinal);
        foreach (StyleVarFields.Field field in StyleVarFields.All)
        {
            resolvedVars[field.Id] = vars.TryGetValue(field.Id, out StyleVarValue value) ? value : rootPreset.Vars[field.Id];
        }

        return new ResolvedStyle
        {
            Name = document.Name,
            Palette = resolvedPalette,
            Colors = resolvedColors,
            Tokens = resolvedTokens,
            Sizes = resolvedSizes,
            Vars = resolvedVars,
            FontScale = fontScale ?? 1.0f,
            FontSlots = fontSlots,
            Problems = problems,
        };
    }

    private static (IReadOnlyList<StyleDocument> Chain, string RootPreset) BuildChain(
        StyleDocument document, Func<string, StyleDocument?> lookupParent, List<StyleProblem> problems)
    {
        List<StyleDocument> chain = new();
        HashSet<string> visited = new(StringComparer.Ordinal);
        StyleDocument? current = document;
        string rootPreset = "Dark";

        while (current is not null)
        {
            if (!visited.Add(current.Name))
            {
                problems.Add(new StyleProblem("extends", $"Cycle detected resolving '{document.Name}' (revisited '{current.Name}'); falling back to Dark."));
                rootPreset = "Dark";
                break;
            }

            chain.Add(current);

            string parentName = string.IsNullOrWhiteSpace(current.Extends) ? "Dark" : current.Extends!;
            if (BuiltInStyles.IsBuiltIn(parentName))
            {
                rootPreset = parentName;
                break;
            }

            StyleDocument? parent = lookupParent(parentName);
            if (parent is null)
            {
                if (!string.IsNullOrWhiteSpace(current.Extends))
                {
                    problems.Add(new StyleProblem("extends", $"Style '{current.Name}' extends unknown style '{parentName}'; falling back to Dark."));
                }

                rootPreset = "Dark";
                break;
            }

            current = parent;
        }

        chain.Reverse();
        return (chain, rootPreset);
    }

    private static StyleFontSlotValue MergeFontSlot(StyleFontSlotValue baseSlot, StyleFontSlotValue overrideSlot)
    {
        if (overrideSlot.File is not null)
        {
            return new StyleFontSlotValue { File = overrideSlot.File, Size = overrideSlot.Size ?? baseSlot.Size };
        }

        if (overrideSlot.Family is not null)
        {
            bool sameFamily = overrideSlot.Family == baseSlot.Family;
            return new StyleFontSlotValue
            {
                Family = overrideSlot.Family,
                Weight = overrideSlot.Weight ?? (sameFamily ? baseSlot.Weight : null),
                Italic = overrideSlot.Italic ?? (sameFamily ? baseSlot.Italic : null),
                Size = overrideSlot.Size ?? baseSlot.Size,
            };
        }

        return new StyleFontSlotValue
        {
            Family = baseSlot.Family,
            Weight = baseSlot.Weight,
            Italic = baseSlot.Italic,
            File = baseSlot.File,
            Size = overrideSlot.Size ?? baseSlot.Size,
        };
    }

    private sealed class ColorGraph
    {
        private readonly IReadOnlyDictionary<string, StyleColorValue> _palette;
        private readonly IReadOnlyDictionary<string, StyleColorValue> _colors;
        private readonly IReadOnlyDictionary<string, StyleColorValue> _tokens;
        private readonly IReadOnlyDictionary<ImGuiCol, Vector4> _builtInColors;
        private readonly IReadOnlyDictionary<string, StyleColorValue> _tokenDefaults;
        private readonly List<StyleProblem> _problems;

        public ColorGraph(
            IReadOnlyDictionary<string, StyleColorValue> palette,
            IReadOnlyDictionary<string, StyleColorValue> colors,
            IReadOnlyDictionary<string, StyleColorValue> tokens,
            IReadOnlyDictionary<ImGuiCol, Vector4> builtInColors,
            IReadOnlyDictionary<string, StyleColorValue> tokenDefaults,
            List<StyleProblem> problems)
        {
            _palette = palette;
            _colors = colors;
            _tokens = tokens;
            _builtInColors = builtInColors;
            _tokenDefaults = tokenDefaults;
            _problems = problems;
        }

        public Vector4 ResolveImGuiColor(ImGuiCol col, HashSet<string> visiting)
        {
            string name = col.ToString();
            return _colors.TryGetValue(name, out StyleColorValue? value)
                ? ResolveValue(value, $"imgui:{name}", visiting)
                : _builtInColors[col];
        }

        public Vector4 ResolvePalette(string name, HashSet<string> visiting)
        {
            if (!_palette.TryGetValue(name, out StyleColorValue? value))
            {
                _problems.Add(new StyleProblem($"palette.{name}", "Unknown palette entry."));
                return MissingColor;
            }

            return ResolveValue(value, $"palette:{name}", visiting);
        }

        public Vector4 ResolveToken(string id, HashSet<string> visiting)
        {
            if (_tokens.TryGetValue(id, out StyleColorValue? value))
            {
                return ResolveValue(value, $"token:{id}", visiting);
            }

            if (_tokenDefaults.TryGetValue(id, out StyleColorValue? def))
            {
                return ResolveValue(def, $"token-default:{id}", visiting);
            }

            _problems.Add(new StyleProblem($"tokens.{id}", "No value and no code-declared default."));
            return MissingColor;
        }

        private Vector4 ResolveValue(StyleColorValue value, string graphKey, HashSet<string> visiting)
        {
            if (value is StyleColorLiteral literal)
            {
                return literal.Color;
            }

            StyleColorReference reference = (StyleColorReference)value;
            if (!visiting.Add(graphKey))
            {
                _problems.Add(new StyleProblem(graphKey, $"Reference cycle detected resolving '{reference.Target}'."));
                return MissingColor;
            }

            Vector4 resolved = ResolveTarget(reference.Target, visiting);
            visiting.Remove(graphKey);

            if (reference.AlphaOverride is { } alpha) resolved.W = alpha;
            if (reference.Lighten is { } lighten) resolved = Lighten(resolved, lighten);
            if (reference.Darken is { } darken) resolved = Darken(resolved, darken);
            return resolved;
        }

        private Vector4 ResolveTarget(string target, HashSet<string> visiting)
        {
            string key = target[1..];
            if (target[0] == '$')
            {
                return ResolvePalette(key, visiting);
            }

            if (Enum.TryParse(key, out ImGuiCol col) && col != ImGuiCol.COUNT)
            {
                return ResolveImGuiColor(col, visiting);
            }

            return ResolveToken(key, visiting);
        }

        private static Vector4 Lighten(Vector4 color, float t) => new(
            color.X + (1f - color.X) * t,
            color.Y + (1f - color.Y) * t,
            color.Z + (1f - color.Z) * t,
            color.W);

        private static Vector4 Darken(Vector4 color, float t) => new(
            color.X * (1f - t),
            color.Y * (1f - t),
            color.Z * (1f - t),
            color.W);
    }

    private static readonly Vector4 MissingColor = new(1f, 0f, 1f, 1f);
}
