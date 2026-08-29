using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A material *description*: which <see cref="IMeshMaterialType"/> renders it, and that type's
/// parameter values. Neither a surface nor a loader ever builds a Godot <see cref="Material"/>
/// directly — they describe one here and hand it to <see cref="MeshMaterialSystem.Build"/>.
/// </summary>
public sealed record MeshMaterial(string TypeId, MeshParameterValues Values)
{
    /// <summary>Cache/equality key: identical type and values render identically.</summary>
    public string Key => $"{TypeId}|{Values.Serialize()}";

    public MeshMaterial With(MeshParameter parameter, string value) => WithClone(clone => clone.Set(parameter, value));

    public MeshMaterial With(MeshParameter parameter, float value) => WithClone(clone => clone.Set(parameter, value));

    public MeshMaterial With(MeshParameter parameter, int value) => WithClone(clone => clone.Set(parameter, value));

    public MeshMaterial With(MeshParameter parameter, bool value) => WithClone(clone => clone.Set(parameter, value));

    public MeshMaterial With(MeshParameter parameter, Color value) => WithClone(clone => clone.Set(parameter, value));

    private MeshMaterial WithClone(System.Action<MeshParameterValues> mutate)
    {
        MeshParameterValues clone = Values.Clone();
        mutate(clone);
        return this with { Values = clone };
    }
}

/// <summary>
/// The built-in, format-agnostic material vocabulary — what every loader used before formats had
/// their own material types. OBJ, glTF and format-agnostic procedural meshes render through this.
/// </summary>
public static class StandardMeshMaterial
{
    public const string TypeId = "builtin.material.standard";

    public static readonly MeshParameter Texture = MeshParameter.Texture("texture", "Texture", "Albedo texture.");

    public static readonly MeshParameter Albedo = MeshParameter.Color("albedo", "Albedo", Colors.White, "Base color, tinted by the texture if one is set.");

    public static readonly MeshParameter Blend = MeshParameter.Choice(
        "blend",
        "Blend",
        [
            new MeshParameterOption("opaque", "Opaque"),
            new MeshParameterOption("alpha_cutout", "Alpha Cutout"),
            new MeshParameterOption("alpha_blend", "Alpha Blend"),
            new MeshParameterOption("additive", "Additive"),
            new MeshParameterOption("modulate", "Modulate"),
            new MeshParameterOption("modulate2x", "Modulate 2x"),
        ],
        "opaque");

    public static readonly MeshParameter TwoSided = MeshParameter.Bool("two_sided", "Two Sided", false);

    public static readonly MeshParameter Unlit = MeshParameter.Bool("unlit", "Unlit", false);

    public static readonly MeshParameter DepthWrite = MeshParameter.Bool("depth_write", "Depth Write", true);

    public static readonly MeshParameter VertexColor = MeshParameter.Bool("vertex_color", "Use Vertex Color", false);

    public static readonly MeshParameter RepeatTexture = MeshParameter.Bool("repeat_texture", "Repeat Texture", true);

    public static readonly MeshParameter AlphaCutoff = MeshParameter.Float("alpha_cutoff", "Alpha Cutoff", 0.5f, 0.0f, 1.0f);

    public static readonly MeshParameter SortPriority = MeshParameter.Int("sort_priority", "Sort Priority", 0, -128, 127);

    public static readonly IReadOnlyList<MeshParameter> Parameters = MeshParameter.List(
        Texture, Albedo, Blend, TwoSided, Unlit, DepthWrite, VertexColor, RepeatTexture, AlphaCutoff, SortPriority);

    /// <summary>Builds a description without going through the raw parameter/value API.</summary>
    public static MeshMaterial Describe(
        string texture = "",
        Color? albedo = null,
        string blend = "opaque",
        bool twoSided = false,
        bool unlit = false,
        bool depthWrite = true,
        bool vertexColor = false,
        bool repeatTexture = true,
        float alphaCutoff = 0.5f,
        int sortPriority = 0)
    {
        var values = new MeshParameterValues();
        values.Set(Texture, texture);
        values.Set(Albedo, albedo ?? Colors.White);
        values.Set(Blend, blend);
        values.Set(TwoSided, twoSided);
        values.Set(Unlit, unlit);
        values.Set(DepthWrite, depthWrite);
        values.Set(VertexColor, vertexColor);
        values.Set(RepeatTexture, repeatTexture);
        values.Set(AlphaCutoff, alphaCutoff);
        values.Set(SortPriority, sortPriority);
        return new MeshMaterial(TypeId, values);
    }
}
