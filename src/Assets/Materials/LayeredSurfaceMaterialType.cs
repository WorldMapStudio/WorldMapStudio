using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Renders <see cref="LayeredSurfaceMaterial"/> descriptions through a custom shader (unlike
/// <see cref="StandardMeshMaterialType"/>'s <see cref="StandardMaterial3D"/>) — matcap reflection,
/// a second detail layer, interior/exterior ambient blending and the shared fog function all need
/// fragment-level control <see cref="StandardMaterial3D"/> has no parameters for.
///
/// <see cref="Blend"/>/<see cref="LayeredSurfaceMaterial.TwoSided"/>/<see cref="LayeredSurfaceMaterial.Unlit"/>/
/// <see cref="LayeredSurfaceMaterial.DepthWrite"/> select Godot <c>render_mode</c> keywords, which are
/// compile-time shader source, not runtime uniforms — so unlike every other parameter here, changing one
/// of those four switches to a different (lazily built, cached) <see cref="Shader"/> variant rather than
/// just a different uniform value on the same shader.
/// </summary>
[Subsystem(nameof(MeshMaterialSystem))]
public sealed class LayeredSurfaceMaterialType : IMeshMaterialType
{
    public LayeredSurfaceMaterialType(MeshMaterialSystem system)
    {
    }

    public string Id => LayeredSurfaceMaterial.TypeId;

    public string DisplayName => "Layered Surface";

    public string Description => "Generic multi-layer material: detail texture, matcap reflection, emissive, UV scroll, interior/exterior ambient.";

    public int Version => 1;

    public IReadOnlyList<MeshParameter> Parameters => LayeredSurfaceMaterial.Parameters;

    public Material Build(in MeshMaterialBuildContext context)
    {
        string blend = context.Choice(LayeredSurfaceMaterial.Blend);
        bool twoSided = context.Bool(LayeredSurfaceMaterial.TwoSided);
        bool unlit = context.Bool(LayeredSurfaceMaterial.Unlit);
        bool depthWrite = context.Bool(LayeredSurfaceMaterial.DepthWrite);

        var material = new ShaderMaterial { Shader = ShaderFor(blend, twoSided, unlit, depthWrite) };

        bool alphaModulatesBlend = context.Bool(LayeredSurfaceMaterial.AlphaModulatesBlend);

        material.SetShaderParameter("tint", context.Color(LayeredSurfaceMaterial.Tint));
        material.SetShaderParameter("alpha_mode", AlphaMode(blend, alphaModulatesBlend));
        material.SetShaderParameter("alpha_cutoff", context.Float(LayeredSurfaceMaterial.AlphaCutoff));
        material.SetShaderParameter("fog_blend_mode", FogBlendMode(blend));
        material.SetShaderParameter("unfogged", context.Bool(LayeredSurfaceMaterial.Unfogged));
        material.SetShaderParameter("vertex_color_tint", context.Bool(LayeredSurfaceMaterial.VertexColorTint));
        material.SetShaderParameter("detail_blend", context.Float(LayeredSurfaceMaterial.DetailBlend));
        material.SetShaderParameter("matcap_strength", context.Float(LayeredSurfaceMaterial.MatcapStrength));
        material.SetShaderParameter("emissive_color", context.Color(LayeredSurfaceMaterial.EmissiveColor));
        material.SetShaderParameter("emissive_strength", context.Float(LayeredSurfaceMaterial.EmissiveStrength));
        material.SetShaderParameter("uv_scroll", new Vector2(
            context.Float(LayeredSurfaceMaterial.UvScrollX), context.Float(LayeredSurfaceMaterial.UvScrollY)));

        AssignTextureAsync(context.Assets, material, "albedo_tex", null, context.Texture(LayeredSurfaceMaterial.Texture));
        AssignTextureAsync(context.Assets, material, "detail_tex", "has_detail", context.Texture(LayeredSurfaceMaterial.DetailTexture));
        AssignTextureAsync(context.Assets, material, "matcap_tex", "has_matcap", context.Texture(LayeredSurfaceMaterial.MatcapTexture));

        return material;
    }

    // 0 = force fully opaque (ignore texture alpha), 1 = alpha-scissor cutout, 2 = pass texture alpha
    // through for blend/additive compositing. A runtime uniform rather than another render_mode axis:
    // unlike blend_mix/blend_add/blend_mul, none of these three need different compile-time shader text.
    // additive/modulate specifically defer to alphaModulatesBlend rather than always passing texture
    // alpha through, so a format that distinguishes "flat add" from "alpha-scaled add" (e.g. WoW's
    // no_add_alpha vs add) can express both through this one generic material.
    private static int AlphaMode(string blend, bool alphaModulatesBlend) => blend switch
    {
        "alpha_cutout" => 1,
        "alpha_blend" => 2,
        "additive" or "modulate" or "modulate2x" => alphaModulatesBlend ? 2 : 0,
        _ => 0,
    };

    // Matches EnvironmentShaderLibrary.FogFunctionCode's blend_mode parameter: 0 normal, 1 additive
    // (fog color pushed toward black), 2 multiply (pushed toward white).
    private static int FogBlendMode(string blend) => blend switch
    {
        "additive" => 1,
        "modulate" or "modulate2x" => 2,
        _ => 0,
    };

    private static void AssignTextureAsync(AssetSystem assets, ShaderMaterial material, string uniformName, string? hasUniformName, string texturePath)
    {
        if (texturePath.Length == 0)
        {
            if (hasUniformName != null)
            {
                material.SetShaderParameter(hasUniformName, false);
            }

            return;
        }

        Task<Texture2D?> pending = assets.LoadTextureAssetAsync(texturePath);
        if (pending.IsCompletedSuccessfully)
        {
            ApplyTexture(material, uniformName, hasUniformName, pending.Result);
            return;
        }

        WorkQueue.Schedule($"Assign Layered Surface Texture ({uniformName})", async work =>
        {
            Texture2D? texture = await pending.ConfigureAwait(false);
            await work.SwitchToMain();
            ApplyTexture(material, uniformName, hasUniformName, texture);
        });
    }

    private static void ApplyTexture(ShaderMaterial material, string uniformName, string? hasUniformName, Texture2D? texture)
    {
        if (texture != null)
        {
            material.SetShaderParameter(uniformName, texture);
        }

        if (hasUniformName != null)
        {
            material.SetShaderParameter(hasUniformName, texture != null);
        }
    }

    // Keyed on exactly the four render_mode-affecting parameters, not the material's full content —
    // multiple visually-different materials (different textures/tints/etc.) sharing the same render_mode
    // combination share one compiled Shader, same reasoning as every other cache in this codebase.
    private static readonly Dictionary<string, Shader> ShaderCache = new();

    private static Shader ShaderFor(string blend, bool twoSided, bool unlit, bool depthWrite)
    {
        string key = $"{blend}|{twoSided}|{unlit}|{depthWrite}";
        if (ShaderCache.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        var shader = new Shader { Code = BuildShaderCode(blend, twoSided, unlit, depthWrite) };
        ShaderCache[key] = shader;
        return shader;
    }

    private static string BuildShaderCode(string blend, bool twoSided, bool unlit, bool depthWrite)
    {
        string cull = twoSided ? "cull_disabled" : "cull_back";
        string shading = unlit ? "unshaded" : "diffuse_burley";
        string blendClause = blend switch
        {
            "additive" => "blend_add",
            "modulate" or "modulate2x" => "blend_mul",
            _ => "blend_mix",
        };

        // Alpha-blended/additive surfaces conventionally skip the depth write regardless of the
        // material's own DepthWrite parameter, matching StandardMeshMaterialType/M2MaterialType's own
        // Transparency-implies-no-depth-write convention.
        bool writesDepth = depthWrite && blend is not ("alpha_blend" or "additive");
        string depthClause = writesDepth ? "depth_draw_opaque" : "depth_draw_never";

        // ambient_light_disabled always: this material always computes its own ambient by hand via
        // wms_blend_interior_ambient (see EnvironmentShaderLibrary), since Godot's automatic ambient/GI
        // has no way to be intercepted or replaced from fragment() — only opted out of entirely.
        string renderMode = $"{cull}, {shading}, {blendClause}, {depthClause}, ambient_light_disabled";

        return $$"""
shader_type spatial;
render_mode {{renderMode}};

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform int alpha_mode = 0; // 0 opaque, 1 cutout, 2 pass texture alpha through
uniform float alpha_cutoff = 0.5;
uniform int fog_blend_mode = 0;
uniform bool unfogged = false;
uniform bool vertex_color_tint = false;

uniform sampler2D detail_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_detail = false;
uniform float detail_blend = 1.0;

uniform sampler2D matcap_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform bool has_matcap = false;
uniform float matcap_strength = 0.5;

uniform vec3 emissive_color : source_color = vec3(0.0, 0.0, 0.0);
uniform float emissive_strength = 1.0;

uniform vec2 uv_scroll = vec2(0.0, 0.0);

""" + EnvironmentShaderLibrary.FogFunctionCode + EnvironmentShaderLibrary.InteriorAmbientBlendCode + EnvironmentShaderLibrary.MatcapReflectionCode + """

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec2 uv = UV + uv_scroll * TIME;
    vec4 tex = texture(albedo_tex, uv) * tint;
    vec3 albedo = tex.rgb;

    if (has_detail) {
        vec4 detail = texture(detail_tex, uv);
        albedo = mix(albedo, albedo * detail.rgb * 2.0, detail_blend * detail.a);
    }

    // COLOR.rgb as precomputed per-vertex light (e.g. a WMO group's rebased MOCV): folded into the
    // ambient term below, not multiplied into albedo — baked vertex light adds, it doesn't tint.
    vec3 precomputed_light = vertex_color_tint ? COLOR.rgb : vec3(0.0);

    if (has_matcap) {
        vec3 view_normal = normalize((VIEW_MATRIX * vec4(world_normal, 0.0)).xyz);
        vec3 matcap_color = texture(matcap_tex, wms_matcap_uv(view_normal)).rgb;
        albedo = mix(albedo, albedo + matcap_color, matcap_strength);
    }

    ALBEDO = albedo;
    ROUGHNESS = 0.9;
    SPECULAR = 0.1;

    if (alpha_mode == 0) {
        ALPHA = 1.0;
    } else if (alpha_mode == 1) {
        ALPHA = tex.a;
        ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;
    } else {
        ALPHA = tex.a;
    }

    // Manual ambient (see InteriorAmbientBlendCode's doc comment for why): COLOR.a is the
    // interior/exterior blend factor, defaulting to 1.0 (fully exterior) on any mesh that carries no
    // real vertex color data at all, so untouched geometry behaves as if this were ordinary ambient.
    // precomputed_light adds on top of ambient before it modulates albedo, matching how the reference
    // renderer accumulates baked vertex light.
    float n_dot_up = clamp(dot(world_normal, vec3(0.0, 1.0, 0.0)), -1.0, 1.0);
    vec3 ambient = wms_blend_interior_ambient(n_dot_up, COLOR.a);
    EMISSION = (ambient + precomputed_light) * albedo + emissive_color * emissive_strength;

    if (!unfogged) {
        vec3 view_vec = world_pos - CAMERA_POSITION_WORLD;
        vec3 fog_color;
        float fog_visibility = wms_evaluate_fog(view_vec, world_pos.y, normalize(view_vec), fog_blend_mode, fog_color);
        FOG = vec4(fog_color, 1.0 - fog_visibility);
    }
}
""";
    }
}
