using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Covers <see cref="StyleDocument"/> parsing and <see cref="StyleResolver"/> resolution —
/// no Godot/ImGui context needed beyond the scratch <c>ImGuiStyle</c> <see cref="BuiltInStyles"/>
/// allocates, so these run on the background thread.</summary>
public static class StyleModelTests
{
    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Parses_literal_and_reference_colors()
    {
        const string json = """
        {
            "version": 1,
            "name": "Test",
            "extends": "Dark",
            "palette": { "accent": "#4A90E2" },
            "colors": {
                "Button": "$accent",
                "ButtonHovered": { "ref": "$accent", "lighten": 0.1 },
                "WindowBg": "#15171CFF"
            }
        }
        """;

        StyleDocument doc = StyleDocument.Parse(json);

        Assert.AreEqual(0, doc.Problems.Count);
        Assert.IsTrue(doc.Palette["accent"] is StyleColorLiteral);
        Assert.IsTrue(doc.Colors["Button"] is StyleColorReference { Target: "$accent" });
        Assert.IsTrue(doc.Colors["ButtonHovered"] is StyleColorReference { Lighten: 0.1f });
        Assert.IsTrue(doc.Colors["WindowBg"] is StyleColorLiteral);
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Unknown_top_level_key_survives_round_trip_and_is_reported()
    {
        const string json = """{ "version": 1, "name": "Test", "extends": "Dark", "fromTheFuture": { "x": 1 } }""";

        StyleDocument doc = StyleDocument.Parse(json);
        Assert.IsTrue(doc.Problems.Any(p => p.Message.Contains("fromTheFuture")));

        StyleDocument reparsed = StyleDocument.Parse(doc.ToJson());
        Assert.IsTrue(reparsed.ToJson().Contains("fromTheFuture"));
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Sparse_round_trip_preserves_values()
    {
        StyleDocument doc = StyleDocument.Parse("""{ "version": 1, "name": "Test", "extends": "Dark" }""");
        doc.Palette["accent"] = new StyleColorLiteral(new Vector4(0.2f, 0.4f, 0.6f, 1f));
        doc.Colors["Button"] = new StyleColorReference("$accent", null, null, null);
        doc.Vars["FrameRounding"] = StyleVarValue.Scalar(3f);
        doc.Vars["FramePadding"] = StyleVarValue.Vector(6f, 4f);

        StyleDocument reparsed = StyleDocument.Parse(doc.ToJson());

        Assert.IsTrue(reparsed.Colors["Button"] is StyleColorReference { Target: "$accent" });
        Assert.AreEqual(3f, reparsed.Vars["FrameRounding"].AsFloat());
        Assert.IsTrue(reparsed.Vars["FramePadding"].IsVector);
        Assert.AreEqual(new Vector2(6f, 4f), reparsed.Vars["FramePadding"].AsVector2());
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Resolves_extends_chain_and_palette_reference()
    {
        StyleDocument dark = StyleDocument.Parse("""{ "version": 1, "name": "Dark" }""");
        StyleDocument midnight = StyleDocument.Parse("""
        {
            "version": 1, "name": "Midnight", "extends": "Dark",
            "palette": { "accent": "#4A90E2" },
            "colors": { "Button": "$accent" }
        }
        """);

        Dictionary<string, StyleDocument> store = new() { ["Dark"] = dark, ["Midnight"] = midnight };
        ResolvedStyle resolved = StyleResolver.Resolve(midnight, name => store.GetValueOrDefault(name));

        Assert.AreEqual(0, resolved.Problems.Count);
        Assert.IsTrue(NearlyEqual(new Vector4(0x4A / 255f, 0x90 / 255f, 0xE2 / 255f, 1f), resolved.Colors[ImGuiCol.Button]));

        // Inherited (not overridden) color still resolves to the Dark preset's own value.
        Vector4 darkText = BuiltInStyles.Find("Dark")!.Colors[ImGuiCol.Text];
        Assert.IsTrue(NearlyEqual(darkText, resolved.Colors[ImGuiCol.Text]));
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Extends_cycle_falls_back_to_dark_with_a_problem()
    {
        StyleDocument a = StyleDocument.Parse("""{ "version": 1, "name": "A", "extends": "B" }""");
        StyleDocument b = StyleDocument.Parse("""{ "version": 1, "name": "B", "extends": "A" }""");
        Dictionary<string, StyleDocument> store = new() { ["A"] = a, ["B"] = b };

        ResolvedStyle resolved = StyleResolver.Resolve(a, name => store.GetValueOrDefault(name));

        Assert.IsTrue(resolved.Problems.Any(p => p.Message.Contains("Cycle")));
        Assert.IsTrue(NearlyEqual(BuiltInStyles.Find("Dark")!.Colors[ImGuiCol.Text], resolved.Colors[ImGuiCol.Text]));
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Missing_parent_falls_back_to_dark_with_a_problem()
    {
        StyleDocument doc = StyleDocument.Parse("""{ "version": 1, "name": "Orphan", "extends": "DoesNotExist" }""");
        ResolvedStyle resolved = StyleResolver.Resolve(doc, _ => null);

        Assert.IsTrue(resolved.Problems.Any(p => p.Message.Contains("unknown style")));
        Assert.IsTrue(NearlyEqual(BuiltInStyles.Find("Dark")!.Colors[ImGuiCol.WindowBg], resolved.Colors[ImGuiCol.WindowBg]));
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Reference_cycle_between_palette_entries_is_reported()
    {
        StyleDocument doc = StyleDocument.Parse("""
        {
            "version": 1, "name": "Loopy", "extends": "Dark",
            "palette": { "a": "$b", "b": "$a" },
            "colors": { "Button": "$a" }
        }
        """);

        ResolvedStyle resolved = StyleResolver.Resolve(doc, _ => null);
        Assert.IsTrue(resolved.Problems.Any(p => p.Message.Contains("cycle")));
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Token_without_override_falls_back_to_code_declared_default()
    {
        StyleDocument doc = StyleDocument.Parse("""{ "version": 1, "name": "Test", "extends": "Dark" }""");
        Dictionary<string, StyleColorValue> defaults = new()
        {
            ["text.error"] = new StyleColorLiteral(new Vector4(1f, 0.45f, 0.4f, 1f)),
        };

        ResolvedStyle resolved = StyleResolver.Resolve(doc, _ => null, defaults);

        Assert.IsTrue(NearlyEqual(new Vector4(1f, 0.45f, 0.4f, 1f), resolved.Tokens["text.error"]));
    }

    [EditorTest(Category = "Style")]
    public static void Shipped_styles_parse_and_resolve_without_problems()
    {
        foreach (string name in new[] { "Midnight", "Daybreak" })
        {
            string path = Godot.ProjectSettings.GlobalizePath($"res://styles/{name}.json");
            StyleDocument doc = StyleDocument.Parse(System.IO.File.ReadAllText(path));
            Assert.AreEqual(0, doc.Problems.Count, $"{name}.json had parse problems: {string.Join("; ", doc.Problems)}");

            ResolvedStyle resolved = StyleResolver.Resolve(doc, _ => null);
            Assert.AreEqual(0, resolved.Problems.Count, $"{name}.json had resolve problems: {string.Join("; ", resolved.Problems)}");
        }
    }

    private static bool NearlyEqual(Vector4 a, Vector4 b) => Vector4.Distance(a, b) < 0.001f;
}
