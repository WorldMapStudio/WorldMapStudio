using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Applies <see cref="EnvironmentSystem.Current"/> to a Godot <see cref="Godot.Environment"/>, a
/// <see cref="DirectionalLight3D"/> sun, and a set of camera-pinned model layers (skyboxes, stars).
/// Owned and updated once per frame by <see cref="ViewportWindow"/>. With no sources loaded at all,
/// <see cref="EnvironmentValues"/>'s own defaults still light the scene with a generic sun — only
/// <see cref="ViewSettings.UseEnvironmentLighting"/> off falls back to the flat grey look the editor
/// always used before this system existed.
/// </summary>
public sealed class EnvironmentRenderer
{
    private const int MaxGradientStops = 8;

    // The flat, source-free default the viewport falls back to when no light is driving the scene
    private static readonly Color FlatAmbient = new(0.45f, 0.45f, 0.45f);

    /// <summary>How much of the camera's far plane the fitted sky dome takes up. Just inside it, so
    /// the dome is never clipped, and everything the map streams in is comfortably in front of it.</summary>
    private const float SkyFitFraction = 0.45f;

    private sealed class SkyLayerSlot
    {
        public Node3D? Node;
        public bool Loading;

        /// <summary>Half the model's largest bounds extent, i.e. the dome's radius at scale 1.</summary>
        public float NativeRadius = 1.0f;
    }

    private readonly Camera3D _camera;
    private readonly AssetSystem _assets;
    private readonly MeshMaterialSystem _materials;
    private readonly EnvironmentSystem _environments;
    private readonly ViewSettings _view;

    private readonly ShaderMaterial _skyMaterial;
    private readonly DirectionalLight3D _sun;
    private readonly Node3D _skyLayerRoot;
    private readonly Dictionary<string, SkyLayerSlot> _skyLayerSlots = new();

    private Shader? _skyShader;
    private int _appliedVersion = -1;

    public Godot.Environment Environment { get; }

    public EnvironmentRenderer(SubViewport viewport, Camera3D camera, AssetSystem assets, MeshMaterialSystem materials, EnvironmentSystem environments, ViewSettings view)
    {
        _camera = camera;
        _assets = assets;
        _materials = materials;
        _environments = environments;
        _view = view;

        _skyMaterial = new ShaderMaterial { Shader = SkyShader() };
        var sky = new Sky { SkyMaterial = _skyMaterial };

        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = FlatAmbient,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = FlatAmbient,
            Sky = sky,
            FogEnabled = false,
        };

        _sun = new DirectionalLight3D { Name = "EnvironmentSun", Visible = false };
        viewport.AddChild(_sun);

        _skyLayerRoot = new Node3D { Name = "EnvironmentSkyLayers" };
        viewport.AddChild(_skyLayerRoot);

        RegisterGlobalShaderParameters();
    }

    // Global shader parameters are process-wide RenderingServer state, not tied to this instance's
    // lifetime — GlobalShaderParameterAdd asserts (ERR_FAIL_COND) if a name is already registered, which
    // a second EnvironmentRenderer construction within the same running process (e.g. closing and
    // reopening a project without restarting the app) would trigger. Guarded by a static bool, same as
    // WowLiquidMaterialType.EnsureTintUpdater()'s own once-per-process registration.
    //
    // GlobalShaderParameterGet — which would make this check against the engine's actual state instead
    // of a C# assumption — looks like the more robust option, but it's explicitly restricted to the
    // editor ("This function should never be used outside the editor, it can severely damage
    // performance", confirmed by hitting exactly that error here) and WorldMapStudio runs as a plain
    // Godot application, not inside the editor — so it isn't actually usable for this check. A static
    // bool is what's available; this app has no in-process C# hot-reload path that would reset it out
    // from under already-registered native state (unlike an editor plugin), so it's sufficient here.
    //
    // Every consuming shader (terrain splat, sky, liquid, model materials) declares a matching
    // `global uniform` for the ones it needs — see EnvironmentShaderLibrary — and reads a value pushed
    // here every frame by ApplyEnvironmentGlobals, the same mechanism WowLiquidTintUpdater already uses
    // for ocean/river tint.
    private static bool _globalsRegistered;

    private static void RegisterGlobalShaderParameters()
    {
        if (_globalsRegistered)
        {
            return;
        }

        _globalsRegistered = true;

        Add("wms_fog_enabled", RenderingServer.GlobalShaderParameterType.Bool, false);
        Add("wms_fog_color", RenderingServer.GlobalShaderParameterType.Vec3, Vector3.One * 0.5f);
        Add("wms_fog_start", RenderingServer.GlobalShaderParameterType.Float, 0.0f);
        Add("wms_fog_end", RenderingServer.GlobalShaderParameterType.Float, 1000.0f);
        Add("wms_fog_curve", RenderingServer.GlobalShaderParameterType.Float, 1.0f);

        Add("wms_fog_curve_enabled", RenderingServer.GlobalShaderParameterType.Bool, false);
        Add("wms_fog_curve_coeffs", RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        Add("wms_fog_curve_start", RenderingServer.GlobalShaderParameterType.Float, 0.0f);
        Add("wms_fog_curve_end", RenderingServer.GlobalShaderParameterType.Float, 1000.0f);

        Add("wms_height_fog_enabled", RenderingServer.GlobalShaderParameterType.Bool, false);
        Add("wms_height_fog_color", RenderingServer.GlobalShaderParameterType.Vec3, Vector3.One * 0.5f);
        Add("wms_height_fog_coeffs", RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        Add("wms_height_fog_reference_z", RenderingServer.GlobalShaderParameterType.Float, 0.0f);
        Add("wms_height_fog_scale", RenderingServer.GlobalShaderParameterType.Float, 1.0f);

        Add("wms_sun_glow_color", RenderingServer.GlobalShaderParameterType.Vec3, Vector3.One);
        Add("wms_sun_glow_cos_angle", RenderingServer.GlobalShaderParameterType.Float, 1.0f);
        Add("wms_sun_glow_exponent", RenderingServer.GlobalShaderParameterType.Float, 3.0f);
        Add("wms_sun_direction", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.35f, -0.85f, 0.4f));

        Add("wms_exterior_ambient_color", RenderingServer.GlobalShaderParameterType.Vec3, Vector3.One * 0.28f);
        Add("wms_exterior_ambient_energy", RenderingServer.GlobalShaderParameterType.Float, 1.0f);
        Add("wms_interior_ambient_color", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.25f, 0.24f, 0.26f));
        Add("wms_interior_horizon_color", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.3f, 0.29f, 0.31f));
        Add("wms_interior_ground_color", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.18f, 0.17f, 0.19f));
        Add("wms_interior_direct_color", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.35f, 0.33f, 0.3f));

        Add("wms_terrain_specular_intensity", RenderingServer.GlobalShaderParameterType.Float, 1.0f);

        return;

        static void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant defaultValue) =>
            RenderingServer.GlobalShaderParameterAdd(name, type, defaultValue);
    }

    /// <summary>
    /// Runs one frame: repositions camera-pinned sky layers, then reapplies the blended environment
    /// only when it actually changed (<see cref="EnvironmentSystem.Version"/> moved).
    /// </summary>
    /// <param name="cameraPosition">
    /// The camera's live position for this frame — the same value passed to
    /// <see cref="EnvironmentSystem.Update"/>, not <c>_camera.GlobalPosition</c>. The Godot camera
    /// node's own transform is only synced later in <see cref="ViewportWindow.DrawContent"/> (via
    /// <c>FlyCamera.ApplyTo</c>), so reading it here would pin sky layers a frame behind — fine while
    /// stationary, but the pinned dome visibly trails while flying.
    /// </param>
    public void Update(Vector3 cameraPosition)
    {
        _skyLayerRoot.GlobalPosition = cameraPosition;

        if (!_view.UseEnvironmentLighting)
        {
            ApplyFlatDefault();
            _appliedVersion = -1;
            return;
        }

        if (_environments.Version == _appliedVersion)
        {
            return;
        }

        _appliedVersion = _environments.Version;
        EnvironmentValues values = _environments.Current;
        ApplySky(values);
        ApplyAmbient(values);
        ApplySun(values);
        ApplyFog(values);
        ApplyEnvironmentGlobals(values);
        ApplyViewDistance(values);
        ApplySkyLayers(values);
    }

    private void ApplyFlatDefault()
    {
        Environment.BackgroundMode = Godot.Environment.BGMode.Color;
        Environment.BackgroundColor = FlatAmbient;
        Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        Environment.AmbientLightColor = FlatAmbient;
        Environment.AmbientLightEnergy = 1.0f;
        Environment.FogEnabled = false;
        _sun.Visible = false;

        RenderingServer.GlobalShaderParameterSet("wms_fog_enabled", false);
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve_enabled", false);
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_enabled", false);
        RenderingServer.GlobalShaderParameterSet("wms_exterior_ambient_color", ToVec3(FlatAmbient));
        RenderingServer.GlobalShaderParameterSet("wms_exterior_ambient_energy", 1.0f);
        RenderingServer.GlobalShaderParameterSet("wms_interior_ambient_color", ToVec3(FlatAmbient));
        RenderingServer.GlobalShaderParameterSet("wms_interior_horizon_color", ToVec3(FlatAmbient));
        RenderingServer.GlobalShaderParameterSet("wms_interior_ground_color", ToVec3(FlatAmbient));
        RenderingServer.GlobalShaderParameterSet("wms_interior_direct_color", Vector3.Zero);

        foreach (SkyLayerSlot slot in _skyLayerSlots.Values)
        {
            if (slot.Node != null)
            {
                slot.Node.Visible = false;
            }
        }
    }

    private void ApplySky(EnvironmentValues values)
    {
        if (values.SkyGradient.Count == 0)
        {
            Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            Environment.BackgroundColor = values.AmbientColor;
            _skyMaterial.SetShaderParameter("has_gradient", false);
            return;
        }

        Environment.BackgroundMode = Godot.Environment.BGMode.Sky;

        // Stops run top (highest elevation) to bottom, matching the shader's descending-elevation scan.
        SkyGradientStop[] sorted = values.SkyGradient.OrderByDescending(stop => stop.ElevationDegrees).ToArray();
        int count = Mathf.Min(sorted.Length, MaxGradientStops);

        var colors = new Godot.Collections.Array<Color>();
        var elevations = new float[MaxGradientStops];
        for (int i = 0; i < MaxGradientStops; i++)
        {
            if (i < count)
            {
                colors.Add(sorted[i].Color);
                elevations[i] = sorted[i].ElevationDegrees;
            }
            else
            {
                colors.Add(Colors.Black);
                elevations[i] = -90.0f;
            }
        }

        _skyMaterial.SetShaderParameter("has_gradient", true);
        _skyMaterial.SetShaderParameter("stop_count", count);
        _skyMaterial.SetShaderParameter("stop_colors", colors);
        _skyMaterial.SetShaderParameter("stop_elevations", elevations);
    }

    private void ApplyAmbient(EnvironmentValues values)
    {
        Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        Environment.AmbientLightColor = values.AmbientColor;
        Environment.AmbientLightEnergy = values.AmbientEnergy;
    }

    private void ApplySun(EnvironmentValues values)
    {
        Vector3 direction = values.SunDirection;
        if (direction.LengthSquared() < 0.0001f)
        {
            _sun.Visible = false;
            return;
        }

        direction = direction.Normalized();

        // LookAtFromPosition needs an up vector that isn't parallel to the look direction — the
        // default straight-down sun is exactly that degenerate case.
        Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;
        _sun.Visible = true;
        _sun.LookAtFromPosition(Vector3.Zero, direction, up);
        _sun.LightColor = values.SunColor;
        _sun.LightEnergy = values.SunEnergy;
    }

    // Godot's own environment fog is never used (Environment.FogEnabled always stays false, set once in
    // the constructor and in ApplyFlatDefault): fog is instead computed per-fragment by every consuming
    // shader itself via EnvironmentShaderLibrary.FogFunctionCode, driven by the global shader parameters
    // ApplyEnvironmentGlobals pushes below. That's what lets a single fog model support the curve, height,
    // and sun-glow terms Godot's built-in depth fog has no equivalent for, and lets model materials skip
    // fog per-material (see Phase 3's "unfogged" bypass) the way the built-in fog never could.
    private void ApplyFog(EnvironmentValues values)
    {
        RenderingServer.GlobalShaderParameterSet("wms_fog_enabled", values.FogEnabled);
        RenderingServer.GlobalShaderParameterSet("wms_fog_color", ToVec3(values.FogColor));
        RenderingServer.GlobalShaderParameterSet("wms_fog_start", values.FogStart);
        RenderingServer.GlobalShaderParameterSet("wms_fog_end", Mathf.Max(values.FogStart + 0.1f, values.FogEnd));
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve", Mathf.Max(0.01f, values.FogCurve));

        bool hasCurve = values.FogCurveCoefficients.Count == 4;
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve_enabled", hasCurve);
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve_coeffs", hasCurve ? ToVec4(values.FogCurveCoefficients) : Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve_start", values.FogCurveStart);
        RenderingServer.GlobalShaderParameterSet("wms_fog_curve_end", Mathf.Max(values.FogCurveStart + 0.1f, values.FogCurveEnd));

        bool hasHeightFog = values.HeightFogCoefficients.Count == 4;
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_enabled", hasHeightFog);
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_color", ToVec3(values.HeightFogColor));
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_coeffs", hasHeightFog ? ToVec4(values.HeightFogCoefficients) : Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_reference_z", values.HeightFogReferenceZ);
        RenderingServer.GlobalShaderParameterSet("wms_height_fog_scale", values.HeightFogScale);
    }

    private void ApplyEnvironmentGlobals(EnvironmentValues values)
    {
        RenderingServer.GlobalShaderParameterSet("wms_sun_glow_color", ToVec3(values.SunGlowColor));
        RenderingServer.GlobalShaderParameterSet("wms_sun_glow_cos_angle", values.SunGlowCosAngle);
        RenderingServer.GlobalShaderParameterSet("wms_sun_glow_exponent", values.SunGlowExponent);
        RenderingServer.GlobalShaderParameterSet("wms_sun_direction", values.SunDirection);

        RenderingServer.GlobalShaderParameterSet("wms_exterior_ambient_color", ToVec3(values.AmbientColor));
        RenderingServer.GlobalShaderParameterSet("wms_exterior_ambient_energy", values.AmbientEnergy);
        RenderingServer.GlobalShaderParameterSet("wms_interior_ambient_color", ToVec3(values.InteriorAmbientColor));
        RenderingServer.GlobalShaderParameterSet("wms_interior_horizon_color", ToVec3(values.InteriorHorizonColor));
        RenderingServer.GlobalShaderParameterSet("wms_interior_ground_color", ToVec3(values.InteriorGroundColor));
        RenderingServer.GlobalShaderParameterSet("wms_interior_direct_color", ToVec3(values.InteriorDirectColor));

        RenderingServer.GlobalShaderParameterSet("wms_terrain_specular_intensity", values.TerrainSpecularIntensity);
    }

    // sRGB -> linear, matching WowLiquidTintUpdater's convention for every colour pushed as a global
    // shader parameter: ALBEDO and the values it's mixed with in-shader are linear, so an un-converted
    // sRGB colour would read too bright/washed out once blended in.
    private static Vector3 ToVec3(Color color)
    {
        Color linear = color.SrgbToLinear();
        return new Vector3(linear.R, linear.G, linear.B);
    }

    private static Vector4 ToVec4(IReadOnlyList<float> values) =>
        new(values[0], values[1], values[2], values[3]);

    private void ApplyViewDistance(EnvironmentValues values)
    {
        if (values.ViewDistance > 0.0f)
        {
            _camera.Far = values.ViewDistance;
        }
    }

    // Camera-pinned models (skybox, stars): loaded lazily per path and kept until the path drops out
    // of Current.SkyLayers entirely. No per-material alpha driving — the weight only gates visibility
    // for now (see LightingPlan.md's phase 5 risk notes); a plugin's own material can refine this.
    private void ApplySkyLayers(EnvironmentValues values)
    {
        var seen = new HashSet<string>();
        foreach (SkyLayer layer in values.SkyLayers)
        {
            seen.Add(layer.ModelPath);
            if (!_skyLayerSlots.TryGetValue(layer.ModelPath, out SkyLayerSlot? slot))
            {
                slot = new SkyLayerSlot();
                _skyLayerSlots[layer.ModelPath] = slot;
            }

            if (slot.Node == null && !slot.Loading)
            {
                RequestSkyLayer(layer.ModelPath, slot);
            }

            if (slot.Node != null)
            {
                slot.Node.Visible = layer.Weight > 0.01f;
                slot.Node.Scale = Vector3.One * FitScale(slot, layer.Scale);
            }
        }

        foreach (string path in _skyLayerSlots.Keys.ToList())
        {
            if (seen.Contains(path))
            {
                continue;
            }

            _skyLayerSlots[path].Node?.QueueFree();
            _skyLayerSlots.Remove(path);
        }
    }

    /// <summary>Frees every sky layer node and forgets what was last applied, so a world reload
    /// leaves nothing behind and the next real <see cref="Update"/> rebuilds from scratch. A layer
    /// still loading when this runs simply has nowhere to attach once it completes — <see cref="AttachSkyLayer"/>
    /// checks the slot is still the one it started with before touching it.</summary>
    public void Unload()
    {
        foreach (SkyLayerSlot slot in _skyLayerSlots.Values)
        {
            slot.Node?.QueueFree();
        }

        _skyLayerSlots.Clear();
        _appliedVersion = -1;
    }

    /// <summary>
    /// Sizes the dome to sit just inside the camera's far plane, so ordinary depth testing puts it
    /// behind every bit of world geometry. A sky model's own native size is arbitrary — the WotLK
    /// domes are only ~78 units in radius — and at that size the dome is *nearer* than the terrain and
    /// simply occludes it, which is why this fits to the view distance rather than applying the
    /// model's scale directly. <paramref name="relativeScale"/> is the source's own multiplier on top
    /// of that fit (see <see cref="SkyLayer.Scale"/>).
    /// </summary>
    private float FitScale(SkyLayerSlot slot, float relativeScale)
    {
        float target = _camera.Far * SkyFitFraction;
        float fit = target / Mathf.Max(0.001f, slot.NativeRadius);
        return fit * Mathf.Max(0.001f, relativeScale);
    }

    private void RequestSkyLayer(string path, SkyLayerSlot slot)
    {
        slot.Loading = true;
        Task<ModelAsset?> task = _assets.LoadModelAssetAsync(path);
        if (task.IsCompletedSuccessfully)
        {
            AttachSkyLayer(path, slot, task.Result);
            return;
        }

        WorkQueue.Schedule($"Load Sky Layer {path}", async work =>
        {
            ModelAsset? model = await task.ConfigureAwait(false);
            await work.SwitchToMain();
            AttachSkyLayer(path, slot, model);
        });
    }

    private void AttachSkyLayer(string path, SkyLayerSlot slot, ModelAsset? model)
    {
        slot.Loading = false;

        // The layer may have dropped out of use (or been replaced by a newer slot instance for the
        // same path) while the load was in flight.
        if (model == null || !_skyLayerSlots.TryGetValue(path, out SkyLayerSlot? current) || !ReferenceEquals(current, slot))
        {
            return;
        }

        Node3D node = model.Instantiate(_materials);
        node.Name = $"SkyLayer_{path.GetHashCode():x8}";

        // A skybox needs the camera at the dome's true geometric middle, not wherever the model's own
        // local origin happens to sit — the same reason ModelRendererComponent re-centers by bounds
        // rather than trusting a model's authored pivot.
        node.Position = -model.LocalBounds.GetCenter();

        Vector3 size = model.LocalBounds.Size;
        slot.NativeRadius = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * 0.5f;

        ConfigureSkyMaterials(node);

        _skyLayerRoot.AddChild(node);
        slot.Node = node;
    }

    /// <summary>
    /// Makes an instantiated model behave as sky rather than as scenery. Only covers nodes present at
    /// instantiation — a model whose references resolve asynchronously (a WMO's doodads) would not be
    /// reached, which no sky dome does.
    /// </summary>
    private static void ConfigureSkyMaterials(Node node)
    {
        if (node is GeometryInstance3D geometry)
        {
            // Godot's DirectionalLight3D sun shadow-casts by default; Noggit's forward renderer has no
            // shadow mapping at all, so a dome wrapped around the camera would otherwise shadow the
            // whole scene beneath it every frame.
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            if (geometry is MeshInstance3D { MaterialOverride: BaseMaterial3D material })
            {
                // Sky is emissive, not lit: shading a dome with the very sun it depicts darkens it as
                // the day turns. Noggit draws its skyboxes with the M2 "unlit" render state for the
                // same reason.
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;

                // ...and "unfogged", likewise. The dome is fitted out near the far plane, but fog end
                // is a view distance (~180 units for a stock WotLK light), so depth fog would
                // otherwise saturate it into a flat wall of fog colour and hide the sky entirely.
                material.DisableFog = true;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            ConfigureSkyMaterials(child);
        }
    }

    private Shader SkyShader() => _skyShader ??= new Shader { Code = SkyShaderCode };

    // Kept in source rather than a .gdshader resource so it needs no Godot import step, matching
    // LandscapeChunkMesh's splat shader.
    //
    // Colours travel as a plain array uniform rather than a baked gradient texture: a sky shader's
    // background renders through Godot's own radiance-bake pipeline (see Environment.BackgroundMode.Sky
    // process modes), and a texture swapped in via SetShaderParameter almost every frame (this material
    // used to get a brand new ImageTexture on every camera move) could out-run that bake, which read as
    // an unbound/placeholder-magenta sky. Scalar uniforms have no such caching layer to race.
    private const string SkyShaderCode = """
shader_type sky;

uniform bool has_gradient = false;
uniform vec3 flat_color : source_color = vec3(0.3, 0.3, 0.3);
uniform int stop_count = 0;
uniform vec3 stop_colors[8] : source_color;
uniform float stop_elevations[8];

""" + EnvironmentShaderLibrary.FogFunctionCode + EnvironmentShaderLibrary.SunGlowFunctionCode + """

void sky() {
    vec3 result = flat_color;

    if (has_gradient && stop_count > 0) {
        float elevation = degrees(asin(clamp(EYEDIR.y, -1.0, 1.0)));

        if (elevation >= stop_elevations[0]) {
            result = stop_colors[0];
        } else if (elevation <= stop_elevations[stop_count - 1]) {
            result = stop_colors[stop_count - 1];
        } else {
            for (int i = 0; i < stop_count - 1; i++) {
                if (elevation <= stop_elevations[i] && elevation >= stop_elevations[i + 1]) {
                    float span = max(0.0001, stop_elevations[i] - stop_elevations[i + 1]);
                    float t = (stop_elevations[i] - elevation) / span;
                    result = mix(stop_colors[i], stop_colors[i + 1], t);
                }
            }
        }
    }

    // Sun-glow halo: a cubic-falloff blend toward wms_sun_glow_color around the sun direction, on top
    // of the elevation gradient. wms_sun_glow_cos_angle defaults to 1.0 (unreachable) so this is a no-op
    // until a source deliberately configures it.
    result = wms_apply_sun_glow(result, EYEDIR);

    COLOR = result;
}
""";
}
