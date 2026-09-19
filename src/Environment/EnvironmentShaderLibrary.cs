namespace WorldMapStudio;

/// <summary>
/// Shared GLSL fragments consumed by more than one inline shader (the terrain splat shader, the sky
/// shader, the liquid shader, and model materials) so fog/sun-glow/interior-ambient/matcap math has one
/// source of truth instead of drifting copies. Every shader in this codebase is kept as an inline C#
/// string rather than a <c>.gdshader</c> resource "so it needs no Godot import step" (see
/// <c>LandscapeBatchMesh.SplatShaderCode</c>, <c>EnvironmentRenderer.SkyShaderCode</c>,
/// <c>WowLiquidMaterialType.LiquidShaderCode</c>) — this class keeps that convention: consumers
/// string-concatenate the fragments they need into their own shader source rather than <c>#include</c>-ing
/// a resource file.
///
/// This is textual concatenation, not a real module with its own scope — every identifier here (global
/// uniforms, function names, <em>and function parameters/locals</em>) lands in the same flat namespace
/// as whatever the consuming shader declares. Godot's shader compiler treats a function parameter (and
/// possibly a local variable — untested, not worth relying on) reusing a name already declared as a
/// uniform elsewhere in the same file as a hard "Redefinition of 'x'" compile error, not ordinary
/// shadowing. Every identifier below is therefore `wms_`-prefixed, including ones that would be
/// perfectly fine as bare names (`t`, `color`, `dist`, ...) in a genuinely scoped language — the prefix
/// isn't just a style choice here, it's load-bearing.
///
/// The fog/sun-glow globals below are pushed once per frame by <see cref="EnvironmentRenderer"/> via
/// Godot's global shader parameters (<c>RenderingServer.GlobalShaderParameterSet</c>), the same
/// mechanism <c>WowLiquidTintUpdater</c> already uses for ocean/river tint — every consumer reads the
/// same values with no per-material uniform wiring needed.
/// </summary>
public static class EnvironmentShaderLibrary
{
    /// <summary>
    /// Fog globals plus <c>wms_evaluate_fog</c>, which returns a 0..1 visibility (0 = fully fogged) and
    /// writes the fog color to blend toward. Supports two fog modes (a simple start/end/curve falloff,
    /// or a cubic-curve mode selected once real curve data exists) plus a height-fog color/weight mix
    /// and a sun-glow color blend — without adopting any WoW-specific field names, since any
    /// <see cref="IEnvironmentSource"/> plugin can drive these, not just a WoW one.
    ///
    /// The final <c>int</c> argument (an ordinary value the caller passes in, not a global) lets a
    /// translucent material push its fog color toward black/white instead of the scene fog color,
    /// matching how an additively/multiplicatively blended surface actually composites against fog in
    /// the real client — 0 = normal (use scene fog color), 1 = additive-blended surface, 2 =
    /// multiply-blended surface.
    /// </summary>
    public const string FogFunctionCode = """
// --- shared fog state, pushed once per frame by EnvironmentRenderer ---
// Colour globals are plain vec3/vec4 (not `: source_color`) with the sRGB->linear conversion done in
// C# before upload, matching WowLiquidTintUpdater's established pattern for global colour uniforms.
global uniform bool wms_fog_enabled = false;
global uniform vec3 wms_fog_color = vec3(0.5, 0.5, 0.5);
global uniform float wms_fog_start = 0.0;
global uniform float wms_fog_end = 1000.0;
global uniform float wms_fog_curve = 1.0;

global uniform bool wms_fog_curve_enabled = false;
global uniform vec4 wms_fog_curve_coeffs = vec4(0.0);
global uniform float wms_fog_curve_start = 0.0;
global uniform float wms_fog_curve_end = 1000.0;

global uniform bool wms_height_fog_enabled = false;
global uniform vec3 wms_height_fog_color = vec3(0.5, 0.5, 0.5);
global uniform vec4 wms_height_fog_coeffs = vec4(0.0);
global uniform float wms_height_fog_reference_z = 0.0;
global uniform float wms_height_fog_scale = 1.0;

global uniform vec3 wms_sun_glow_color = vec3(1.0, 1.0, 1.0);
global uniform float wms_sun_glow_cos_angle = 1.0;
global uniform float wms_sun_glow_exponent = 3.0;
global uniform vec3 wms_sun_direction = vec3(0.35, -0.85, 0.4);

float wms_fog_cubic(vec4 wms_arg_coeffs, float wms_arg_t) {
    float wms_local_t = clamp(wms_arg_t, 0.0, 1.0);
    return clamp(wms_arg_coeffs.x * wms_local_t * wms_local_t * wms_local_t + wms_arg_coeffs.y * wms_local_t * wms_local_t + wms_arg_coeffs.z * wms_local_t + wms_arg_coeffs.w, 0.0, 1.0);
}

// wms_arg_view_vector: fragment position minus camera position (view-space VERTEX works directly).
// wms_arg_frag_world_y: the fragment's world-space up coordinate, for height fog.
// wms_arg_view_dir: normalize(view_vector), passed in since callers often already have it.
// wms_arg_surface_blend_mode: this material's own blend mode (see the class doc comment above) — kept
// out of the generic "blend_mode" name a consuming shader (e.g. the terrain splat shader, or
// LayeredSurfaceMaterialType's own `fog_blend_mode` uniform) may well already use for something else.
float wms_evaluate_fog(vec3 wms_arg_view_vector, float wms_arg_frag_world_y, vec3 wms_arg_view_dir, int wms_arg_surface_blend_mode, out vec3 wms_out_fog_color) {
    float wms_local_dist = length(wms_arg_view_vector);

    float wms_local_visibility;
    if (wms_fog_curve_enabled) {
        float wms_local_curve_t = (wms_local_dist - wms_fog_curve_start) / max(0.0001, wms_fog_curve_end - wms_fog_curve_start);
        wms_local_visibility = 1.0 - wms_fog_cubic(wms_fog_curve_coeffs, wms_local_curve_t);
    } else {
        float wms_local_span = max(0.0001, wms_fog_end - wms_fog_start);
        float wms_local_simple_t = clamp((wms_local_dist - wms_fog_start) / wms_local_span, 0.0, 1.0);
        wms_local_visibility = 1.0 - pow(wms_local_simple_t, max(0.01, wms_fog_curve));
    }

    vec3 wms_local_color = wms_fog_color;

    if (wms_height_fog_enabled) {
        float wms_local_height_t = (wms_arg_frag_world_y - wms_height_fog_reference_z) * wms_height_fog_scale;
        float wms_local_height_weight = wms_fog_cubic(wms_height_fog_coeffs, wms_local_height_t);
        wms_local_color = mix(wms_local_color, wms_height_fog_color, wms_local_height_weight);
    }

    float wms_local_sun_span = max(0.0001, 1.0 - wms_sun_glow_cos_angle);
    float wms_local_n_dot_sun = clamp((dot(wms_arg_view_dir, normalize(-wms_sun_direction)) - wms_sun_glow_cos_angle) / wms_local_sun_span, 0.0, 1.0);
    if (wms_local_n_dot_sun > 0.0) {
        wms_local_n_dot_sun = pow(wms_local_n_dot_sun, max(1.0, wms_sun_glow_exponent));
        wms_local_color = mix(wms_local_color, wms_sun_glow_color, wms_local_n_dot_sun);
    }

    if (wms_arg_surface_blend_mode == 1) {
        wms_local_color = vec3(0.0);
    } else if (wms_arg_surface_blend_mode == 2) {
        wms_local_color = vec3(1.0);
    }

    wms_out_fog_color = wms_local_color;
    return wms_fog_enabled ? clamp(wms_local_visibility, 0.0, 1.0) : 1.0;
}
""";

    /// <summary>
    /// Sun-glow globals are declared in <see cref="FogFunctionCode"/> (shared with fog); this adds only
    /// the halo function itself, for the sky shader, which wants the glow as a direct color blend on
    /// EYEDIR rather than folded into a fog visibility term.
    /// </summary>
    public const string SunGlowFunctionCode = """
vec3 wms_apply_sun_glow(vec3 wms_arg_base_color, vec3 wms_arg_eye_dir) {
    float wms_local_sun_span = max(0.0001, 1.0 - wms_sun_glow_cos_angle);
    float wms_local_n_dot_sun = clamp((dot(wms_arg_eye_dir, normalize(-wms_sun_direction)) - wms_sun_glow_cos_angle) / wms_local_sun_span, 0.0, 1.0);
    if (wms_local_n_dot_sun <= 0.0) {
        return wms_arg_base_color;
    }

    wms_local_n_dot_sun = pow(wms_local_n_dot_sun, max(1.0, wms_sun_glow_exponent));
    return mix(wms_arg_base_color, wms_sun_glow_color, wms_local_n_dot_sun);
}
""";

    /// <summary>
    /// Interior/exterior ambient blend globals + function.
    ///
    /// Godot's fragment() stage cannot intercept or replace the engine's own automatic ambient/GI
    /// contribution — only <c>render_mode light_only</c>-style direct-light callbacks can be overridden
    /// that way, and ambient isn't delivered through those. A material that wants a genuinely different
    /// (not just dimmed) ambient indoors therefore has to opt out of Godot's automatic ambient entirely
    /// via <c>render_mode ambient_light_disabled</c> and add its own chosen ambient manually as
    /// <c>EMISSION</c> — direct sunlight still reaches the surface normally either way, since disabling
    /// ambient doesn't touch Godot's per-light shading of the scene's <c>DirectionalLight3D</c>. This
    /// function returns exactly that manual replacement value; the caller is expected to have set
    /// <c>ambient_light_disabled</c> and to add the result into <c>EMISSION</c>.
    ///
    /// <c>wms_exterior_ambient_color</c>/<c>_energy</c> mirror <see cref="EnvironmentValues.AmbientColor"/>/
    /// <see cref="EnvironmentValues.AmbientEnergy"/> exactly — the same flat value Godot's own automatic
    /// ambient uses for materials that don't disable it, so a material using this function reads the
    /// same brightness outdoors as one that doesn't, just computed by hand instead of automatically.
    /// </summary>
    public const string InteriorAmbientBlendCode = """
global uniform vec3 wms_exterior_ambient_color = vec3(0.28, 0.28, 0.28);
global uniform float wms_exterior_ambient_energy = 1.0;
global uniform vec3 wms_interior_ambient_color = vec3(0.25, 0.24, 0.26);
global uniform vec3 wms_interior_horizon_color = vec3(0.3, 0.29, 0.31);
global uniform vec3 wms_interior_ground_color = vec3(0.18, 0.17, 0.19);
global uniform vec3 wms_interior_direct_color = vec3(0.35, 0.33, 0.3);

// wms_arg_n_dot_up: dot(world-space normal, vec3(0,1,0)), for the interior hemisphere split.
// wms_arg_interior_blend: 0 = fully interior, 1 = fully exterior (a vertex-alpha channel at its unset
// default of 1.0 reads as "exterior", so geometry with no authored interior/exterior data renders as if
// this function were never called).
vec3 wms_blend_interior_ambient(float wms_arg_n_dot_up, float wms_arg_interior_blend) {
    vec3 wms_local_exterior = wms_exterior_ambient_color * wms_exterior_ambient_energy;
    vec3 wms_local_interior_hemisphere = mix(wms_interior_ground_color, wms_interior_horizon_color, 0.5 + 0.5 * wms_arg_n_dot_up);
    vec3 wms_local_interior = wms_interior_ambient_color + wms_local_interior_hemisphere + wms_interior_direct_color;
    return mix(wms_local_interior, wms_local_exterior, clamp(wms_arg_interior_blend, 0.0, 1.0));
}
""";

    /// <summary>
    /// A generic spherical-environment-map ("matcap") reflection lookup — a view-space-normal-driven UV into a
    /// static environment texture; not WoW-specific.
    /// </summary>
    public const string MatcapReflectionCode = """
vec2 wms_matcap_uv(vec3 wms_arg_view_space_normal) {
    return wms_arg_view_space_normal.xy * 0.5 + 0.5;
}
""";
}
