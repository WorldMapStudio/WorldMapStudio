using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The format-agnostic multi-layer material vocabulary: a primary texture plus an optional second
/// "detail" diffuse layer, a matcap-style environment/metal reflection layer, flat emissive glow,
/// per-material UV scroll, and interior/exterior ambient blending — the prioritized subset of what a
/// richer combiner-style material language (M2's 34 pixel modes, WMO's 21) actually needs to look right
/// without adopting either format's specific vocabulary. A format's own material type (e.g. WoW's M2/WMO
/// material types) maps its own blend/pixel-mode data onto these generic parameters rather than every
/// format re-deriving this shading model itself, exactly how <see cref="StandardMeshMaterial"/> already
/// works for the simpler generic case.
/// </summary>
public static class LayeredSurfaceMaterial
{
    public const string TypeId = "builtin.material.layered_surface";

    public static readonly MeshParameter Texture = MeshParameter.Texture("texture", "Texture", "Primary albedo texture.");

    public static readonly MeshParameter Tint = MeshParameter.Color("tint", "Tint", Colors.White, "Multiplies the primary texture.");

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

    public static readonly MeshParameter AlphaCutoff = MeshParameter.Float("alpha_cutoff", "Alpha Cutoff", 0.5f, 0.0f, 1.0f);

    /// <summary>For the additive/modulate blend modes only: whether the texture's own alpha channel
    /// scales the effect (true), or the whole texture applies regardless of alpha (false) — the
    /// difference between a format's "alpha-modulated add" and "flat add" flavors of additive blending,
    /// without adopting either format's specific naming for it.</summary>
    public static readonly MeshParameter AlphaModulatesBlend = MeshParameter.Bool("alpha_modulates_blend", "Alpha Modulates Blend", true);

    public static readonly MeshParameter TwoSided = MeshParameter.Bool("two_sided", "Two Sided", false);

    public static readonly MeshParameter Unlit = MeshParameter.Bool("unlit", "Unlit", false);

    public static readonly MeshParameter DepthWrite = MeshParameter.Bool("depth_write", "Depth Write", true);

    /// <summary>Skips the shared fog function entirely — the generic analogue of the reference
    /// renderer's per-material "unfogged" flag on effect-style materials.</summary>
    public static readonly MeshParameter Unfogged = MeshParameter.Bool("unfogged", "Unfogged", false);

    /// <summary>Whether vertex COLOR.rgb is added to ambient as precomputed per-vertex light (e.g. a
    /// WMO group's baked MOCV) rather than tinting albedo. Vertex COLOR.a always drives the
    /// interior/exterior ambient blend regardless of this — the two are independent.</summary>
    public static readonly MeshParameter VertexColorTint = MeshParameter.Bool("vertex_color_tint", "Vertex Light", false);

    public static readonly MeshParameter DetailTexture = MeshParameter.Texture("detail_texture", "Detail Texture", "Optional second diffuse layer, multiplied over the primary texture. Empty disables it entirely.");

    public static readonly MeshParameter DetailBlend = MeshParameter.Float("detail_blend", "Detail Blend", 1.0f, 0.0f, 1.0f);

    public static readonly MeshParameter MatcapTexture = MeshParameter.Texture("matcap_texture", "Matcap Texture", "Spherical environment-map reflection texture, looked up by view-space normal. Empty disables it entirely.");

    public static readonly MeshParameter MatcapStrength = MeshParameter.Float("matcap_strength", "Matcap Strength", 0.5f, 0.0f, 1.0f);

    public static readonly MeshParameter EmissiveColor = MeshParameter.Color("emissive_color", "Emissive Color", Colors.Black, "Flat additive glow. Black disables it.");

    public static readonly MeshParameter EmissiveStrength = MeshParameter.Float("emissive_strength", "Emissive Strength", 1.0f, 0.0f, 8.0f);

    public static readonly MeshParameter UvScrollX = MeshParameter.Float("uv_scroll_x", "UV Scroll X", 0.0f, -8.0f, 8.0f, "UV units per second.");

    public static readonly MeshParameter UvScrollY = MeshParameter.Float("uv_scroll_y", "UV Scroll Y", 0.0f, -8.0f, 8.0f, "UV units per second.");

    public static readonly IReadOnlyList<MeshParameter> Parameters = MeshParameter.List(
        Texture, Tint, Blend, AlphaCutoff, AlphaModulatesBlend, TwoSided, Unlit, DepthWrite, Unfogged, VertexColorTint,
        DetailTexture, DetailBlend, MatcapTexture, MatcapStrength, EmissiveColor, EmissiveStrength,
        UvScrollX, UvScrollY);

    /// <summary>Builds a description without going through the raw parameter/value API.</summary>
    public static MeshMaterial Describe(
        string texture = "",
        Color? tint = null,
        string blend = "opaque",
        float alphaCutoff = 0.5f,
        bool alphaModulatesBlend = true,
        bool twoSided = false,
        bool unlit = false,
        bool depthWrite = true,
        bool unfogged = false,
        bool vertexColorTint = false,
        string detailTexture = "",
        float detailBlend = 1.0f,
        string matcapTexture = "",
        float matcapStrength = 0.5f,
        Color? emissiveColor = null,
        float emissiveStrength = 1.0f,
        float uvScrollX = 0.0f,
        float uvScrollY = 0.0f)
    {
        var values = new MeshParameterValues();
        values.Set(Texture, texture);
        values.Set(Tint, tint ?? Colors.White);
        values.Set(Blend, blend);
        values.Set(AlphaCutoff, alphaCutoff);
        values.Set(AlphaModulatesBlend, alphaModulatesBlend);
        values.Set(TwoSided, twoSided);
        values.Set(Unlit, unlit);
        values.Set(DepthWrite, depthWrite);
        values.Set(Unfogged, unfogged);
        values.Set(VertexColorTint, vertexColorTint);
        values.Set(DetailTexture, detailTexture);
        values.Set(DetailBlend, detailBlend);
        values.Set(MatcapTexture, matcapTexture);
        values.Set(MatcapStrength, matcapStrength);
        values.Set(EmissiveColor, emissiveColor ?? Colors.Black);
        values.Set(EmissiveStrength, emissiveStrength);
        values.Set(UvScrollX, uvScrollX);
        values.Set(UvScrollY, uvScrollY);
        return new MeshMaterial(TypeId, values);
    }
}
