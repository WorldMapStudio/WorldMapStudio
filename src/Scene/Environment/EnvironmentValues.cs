using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>One stop of a top-to-bottom sky gradient, at an elevation angle above the horizon.</summary>
public readonly record struct SkyGradientStop(float ElevationDegrees, Color Color);

/// <summary>
/// A model drawn pinned to the camera (a skybox, stars, moons), keyed by its own asset path so
/// contributions from different sources at the same path union into one weight.
/// </summary>
/// <param name="Scale">
/// A <em>relative</em> multiplier, not the model's world scale: the renderer already fits the model
/// to the camera's far plane so it sits behind all world geometry (sky models' native sizes are
/// arbitrary — WotLK's domes are a mere ~78 units in radius, which would occlude the terrain rather
/// than back it). 1 means "the fitted size"; use this only to push a layer nearer or further than
/// another at the same position.
/// </param>
public readonly record struct SkyLayer(string ModelPath, float Weight, float Scale);

/// <summary>
/// Everything a source (a light, a sky volume, ...) can contribute to the viewport's atmosphere: sun,
/// ambient, a sky gradient, fog, and camera-pinned model layers, plus an open bag of named channels
/// the core blends but does not interpret — a plugin's own colours (e.g. water tint) or floats (e.g.
/// cloud density) travel through <see cref="Floats"/>/<see cref="Colors"/> without the core knowing
/// their meaning.
/// </summary>
public sealed record EnvironmentValues
{
    private static readonly IReadOnlyDictionary<string, float> EmptyFloats = new Dictionary<string, float>();
    private static readonly IReadOnlyDictionary<string, Color> EmptyColors = new Dictionary<string, Color>();

    public Color SunColor { get; init; } = Godot.Colors.White;

    /// <summary>
    /// A generic overhead-daylight angle (not derived from any WoW time-of-day formula — that lives in
    /// a plugin's own global <see cref="IEnvironmentSource"/>, e.g. a WoW light). Used whenever nothing
    /// overrides it, so a scene with no environment source placed still reads as lit from an angle
    /// rather than flat-shaded by a straight-down or absent sun.
    /// </summary>
    public Vector3 SunDirection { get; init; } = new(0.35f, -0.85f, 0.4f);

    public float SunEnergy { get; init; } = 1.0f;

    public Color AmbientColor { get; init; } = new(0.28f, 0.28f, 0.28f);

    public float AmbientEnergy { get; init; } = 1.0f;

    /// <summary>Top to bottom by elevation. Empty means "leave the sky background alone".</summary>
    public IReadOnlyList<SkyGradientStop> SkyGradient { get; init; } = [];

    public bool FogEnabled { get; init; }

    public Color FogColor { get; init; } = Godot.Colors.Gray;

    public float FogStart { get; init; }

    public float FogEnd { get; init; } = 1000.0f;

    public float FogCurve { get; init; } = 1.0f;

    /// <summary>
    /// Cubic-curve fog coefficients (in <c>ax³+bx²+cx+d</c> order), evaluated over distance normalized
    /// by <see cref="FogCurveStart"/>/<see cref="FogCurveEnd"/>, as an alternative to the simple
    /// <see cref="FogStart"/>/<see cref="FogEnd"/>/<see cref="FogCurve"/> falloff above — not a
    /// replacement for it. Empty means "no curve data, use the simple falloff", exactly like
    /// <see cref="SkyGradient"/>'s "empty means leave alone" convention, so a source that only ever
    /// knows the simple model (or one built before curve data existed) still renders correctly.
    /// </summary>
    public IReadOnlyList<float> FogCurveCoefficients { get; init; } = [];

    public float FogCurveStart { get; init; }

    public float FogCurveEnd { get; init; } = 1000.0f;

    /// <summary>
    /// A second, independent fog blended in near the ground: <see cref="HeightFogCoefficients"/>
    /// evaluated over a fragment's height relative to <see cref="HeightFogReferenceZ"/> (scaled by
    /// <see cref="HeightFogScale"/>) produces a 0..1 weight that mixes <see cref="HeightFogColor"/> into
    /// the base fog color — not a separate fog pass. Empty coefficients mean "no height fog".
    /// </summary>
    public Color HeightFogColor { get; init; } = Godot.Colors.Gray;

    public IReadOnlyList<float> HeightFogCoefficients { get; init; } = [];

    public float HeightFogReferenceZ { get; init; }

    public float HeightFogScale { get; init; } = 1.0f;

    /// <summary>
    /// The color fog blends toward when a fragment's view direction is near <see cref="SunDirection"/>
    /// (within <see cref="SunGlowCosAngle"/>, falling off by <see cref="SunGlowExponent"/>) — the halo
    /// around the sun a fogged horizon shows in real skies. Defaults to a no-op glow: a cos-angle of 1
    /// (a single, unreachable direction) means nothing actually glows until a source sets it deliberately.
    /// </summary>
    public Color SunGlowColor { get; init; } = Godot.Colors.White;

    public float SunGlowCosAngle { get; init; } = 1.0f;

    public float SunGlowExponent { get; init; } = 3.0f;

    /// <summary>
    /// A separate ambient/direct set for indoor surfaces (a building's interior shouldn't simply
    /// inherit outdoor sky ambient). Consuming materials blend between this and the normal exterior
    /// ambient by their own per-vertex/per-surface factor — <see cref="EnvironmentValues"/> itself just
    /// carries the two endpoints. Defaults equal to a plausible dim interior rather than black, so a
    /// surface that blends toward "interior" before any source configures this still reads as lit.
    /// </summary>
    public Color InteriorAmbientColor { get; init; } = new(0.25f, 0.24f, 0.26f);

    public Color InteriorHorizonColor { get; init; } = new(0.3f, 0.29f, 0.31f);

    public Color InteriorGroundColor { get; init; } = new(0.18f, 0.17f, 0.19f);

    public Color InteriorDirectColor { get; init; } = new(0.35f, 0.33f, 0.3f);

    /// <summary>Global multiplier on terrain specular highlight strength, the generic analogue of a
    /// per-zone specular intensity a source may want to tune (e.g. a stormy zone dampening it).</summary>
    public float TerrainSpecularIntensity { get; init; } = 1.0f;

    /// <summary>Camera far plane a source would like, or 0 to leave it alone.</summary>
    public float ViewDistance { get; init; }

    public IReadOnlyList<SkyLayer> SkyLayers { get; init; } = [];

    public IReadOnlyDictionary<string, float> Floats { get; init; } = EmptyFloats;

    public IReadOnlyDictionary<string, Color> Colors { get; init; } = EmptyColors;

    /// <summary>
    /// Blends <paramref name="over"/> onto <paramref name="under"/> at <paramref name="weight"/>
    /// (0 = all <paramref name="under"/>, 1 = all <paramref name="over"/>). Scalars and colours lerp;
    /// <see cref="SkyGradient"/> lerps stop-by-stop when the counts match and otherwise snaps to
    /// whichever side has more weight; <see cref="SkyLayers"/> union by model path, the incoming
    /// side's weight scaled by <paramref name="weight"/>; <see cref="Floats"/>/<see cref="Colors"/>
    /// lerp per key where both sides define it, and simply pass through where only one does.
    /// </summary>
    public static EnvironmentValues Blend(EnvironmentValues under, EnvironmentValues over, float weight)
    {
        weight = Mathf.Clamp(weight, 0.0f, 1.0f);
        if (weight <= 0.0f)
        {
            return under;
        }

        if (weight >= 1.0f)
        {
            return over;
        }

        return new EnvironmentValues
        {
            SunColor = under.SunColor.Lerp(over.SunColor, weight),
            SunDirection = under.SunDirection.Lerp(over.SunDirection, weight),
            SunEnergy = Mathf.Lerp(under.SunEnergy, over.SunEnergy, weight),
            AmbientColor = under.AmbientColor.Lerp(over.AmbientColor, weight),
            AmbientEnergy = Mathf.Lerp(under.AmbientEnergy, over.AmbientEnergy, weight),
            SkyGradient = BlendGradient(under.SkyGradient, over.SkyGradient, weight),
            FogEnabled = under.FogEnabled || over.FogEnabled,
            FogColor = under.FogColor.Lerp(over.FogColor, weight),
            FogStart = Mathf.Lerp(under.FogStart, over.FogStart, weight),
            FogEnd = Mathf.Lerp(under.FogEnd, over.FogEnd, weight),
            FogCurve = Mathf.Lerp(under.FogCurve, over.FogCurve, weight),
            FogCurveCoefficients = BlendFloatArray(under.FogCurveCoefficients, over.FogCurveCoefficients, weight),
            FogCurveStart = Mathf.Lerp(under.FogCurveStart, over.FogCurveStart, weight),
            FogCurveEnd = Mathf.Lerp(under.FogCurveEnd, over.FogCurveEnd, weight),
            HeightFogColor = under.HeightFogColor.Lerp(over.HeightFogColor, weight),
            HeightFogCoefficients = BlendFloatArray(under.HeightFogCoefficients, over.HeightFogCoefficients, weight),
            HeightFogReferenceZ = Mathf.Lerp(under.HeightFogReferenceZ, over.HeightFogReferenceZ, weight),
            HeightFogScale = Mathf.Lerp(under.HeightFogScale, over.HeightFogScale, weight),
            SunGlowColor = under.SunGlowColor.Lerp(over.SunGlowColor, weight),
            SunGlowCosAngle = Mathf.Lerp(under.SunGlowCosAngle, over.SunGlowCosAngle, weight),
            SunGlowExponent = Mathf.Lerp(under.SunGlowExponent, over.SunGlowExponent, weight),
            InteriorAmbientColor = under.InteriorAmbientColor.Lerp(over.InteriorAmbientColor, weight),
            InteriorHorizonColor = under.InteriorHorizonColor.Lerp(over.InteriorHorizonColor, weight),
            InteriorGroundColor = under.InteriorGroundColor.Lerp(over.InteriorGroundColor, weight),
            InteriorDirectColor = under.InteriorDirectColor.Lerp(over.InteriorDirectColor, weight),
            TerrainSpecularIntensity = Mathf.Lerp(under.TerrainSpecularIntensity, over.TerrainSpecularIntensity, weight),
            ViewDistance = Mathf.Lerp(under.ViewDistance, over.ViewDistance, weight),
            SkyLayers = BlendLayers(under.SkyLayers, over.SkyLayers, weight),
            Floats = BlendDict(under.Floats, over.Floats, weight, Mathf.Lerp),
            Colors = BlendDict(under.Colors, over.Colors, weight, static (a, b, t) => a.Lerp(b, t)),
        };
    }

    private static IReadOnlyList<SkyGradientStop> BlendGradient(
        IReadOnlyList<SkyGradientStop> under, IReadOnlyList<SkyGradientStop> over, float weight)
    {
        if (under.Count == 0)
        {
            return over;
        }

        if (over.Count == 0)
        {
            return under;
        }

        if (under.Count != over.Count)
        {
            return weight >= 0.5f ? over : under;
        }

        var blended = new SkyGradientStop[under.Count];
        for (int i = 0; i < under.Count; i++)
        {
            SkyGradientStop a = under[i];
            SkyGradientStop b = over[i];
            blended[i] = new SkyGradientStop(
                Mathf.Lerp(a.ElevationDegrees, b.ElevationDegrees, weight),
                a.Color.Lerp(b.Color, weight));
        }

        return blended;
    }

    /// <summary>Same "empty means leave alone" convention as <see cref="BlendGradient"/>: a source with
    /// no curve data (e.g. one built before <see cref="FogCurveCoefficients"/>/
    /// <see cref="HeightFogCoefficients"/> existed) passes the other side through untouched rather than
    /// lerping toward zero coefficients, which would silently fade the curve out.</summary>
    private static IReadOnlyList<float> BlendFloatArray(
        IReadOnlyList<float> under, IReadOnlyList<float> over, float weight)
    {
        if (under.Count == 0)
        {
            return over;
        }

        if (over.Count == 0)
        {
            return under;
        }

        if (under.Count != over.Count)
        {
            return weight >= 0.5f ? over : under;
        }

        var blended = new float[under.Count];
        for (int i = 0; i < under.Count; i++)
        {
            blended[i] = Mathf.Lerp(under[i], over[i], weight);
        }

        return blended;
    }

    private static IReadOnlyList<SkyLayer> BlendLayers(
        IReadOnlyList<SkyLayer> under, IReadOnlyList<SkyLayer> over, float weight)
    {
        if (over.Count == 0)
        {
            return under;
        }

        var merged = new Dictionary<string, SkyLayer>(under.Count + over.Count);
        foreach (SkyLayer layer in under)
        {
            merged[layer.ModelPath] = layer;
        }

        foreach (SkyLayer layer in over)
        {
            float scaledWeight = layer.Weight * weight;
            merged[layer.ModelPath] = merged.TryGetValue(layer.ModelPath, out SkyLayer existing)
                ? existing with { Weight = Mathf.Max(existing.Weight, scaledWeight) }
                : layer with { Weight = scaledWeight };
        }

        var result = new List<SkyLayer>(merged.Count);
        foreach (SkyLayer layer in merged.Values)
        {
            if (layer.Weight > 0.0f)
            {
                result.Add(layer);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, T> BlendDict<T>(
        IReadOnlyDictionary<string, T> under,
        IReadOnlyDictionary<string, T> over,
        float weight,
        System.Func<T, T, float, T> lerp)
    {
        if (over.Count == 0)
        {
            return under;
        }

        if (under.Count == 0)
        {
            return over;
        }

        var result = new Dictionary<string, T>(under);
        foreach (KeyValuePair<string, T> pair in over)
        {
            result[pair.Key] = result.TryGetValue(pair.Key, out T? existing)
                ? lerp(existing, pair.Value, weight)
                : pair.Value;
        }

        return result;
    }
}
