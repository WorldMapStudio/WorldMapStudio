using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The blended environment, exposed to JS as <c>wms.environment</c>. Read-only: the sources that drive
/// it are scene components, which are already writable through <c>wms.scene</c>.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class EnvironmentScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "environment";

    public EnvironmentScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>The environment the viewport currently shows.</summary>
    [ScriptFunction]
    public EnvironmentDescriptor Current() => new(_context.Environments.Current);

    /// <summary>The sources contributing to <see cref="Current"/>, with their weights.</summary>
    [ScriptFunction]
    public EnvironmentActiveDescriptor[] Active() =>
        _context.Environments.Active
            .Where(active => (active.Source as SceneComponent)?.Owner != null)
            .Select(active => new EnvironmentActiveDescriptor(
                new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, ((SceneComponent)active.Source).Owner!),
                active.Weight))
            .ToArray();

    /// <summary>The environment at world (x, y, z), at <paramref name="dayFraction"/> (0-1) or the world
    /// clock's when omitted. Does not change what the viewport shows.</summary>
    [ScriptFunction]
    public EnvironmentDescriptor Sample(double x, double y, double z, double? dayFraction = null) =>
        new(_context.Environments.Sample(new Vector3((float)x, (float)y, (float)z), (float?)dayFraction).Current);

    /// <summary>Recomputes the viewport's environment on the next frame.</summary>
    [ScriptFunction]
    public void Invalidate() => _context.Environments.Invalidate();
}

/// <summary>One contributing source, as <c>wms.environment.Active</c> reports it.</summary>
public sealed class EnvironmentActiveDescriptor
{
    public EnvironmentActiveDescriptor(ScriptEntityHandle entity, float weight)
    {
        Entity = entity;
        Weight = weight;
    }

    [ScriptProperty] public ScriptEntityHandle Entity { get; }
    [ScriptProperty] public double Weight { get; }
}

/// <summary>One stop of a sky gradient.</summary>
public sealed class SkyGradientStopDescriptor
{
    public SkyGradientStopDescriptor(SkyGradientStop stop)
    {
        ElevationDegrees = stop.ElevationDegrees;
        Color = EnvironmentDescriptor.Rgb(stop.Color);
    }

    [ScriptProperty] public double ElevationDegrees { get; }
    [ScriptProperty] public double[] Color { get; }
}

/// <summary>One camera-pinned model layer.</summary>
public sealed class SkyLayerDescriptor
{
    public SkyLayerDescriptor(SkyLayer layer)
    {
        ModelPath = layer.ModelPath;
        Weight = layer.Weight;
        Scale = layer.Scale;
    }

    [ScriptProperty] public string ModelPath { get; }
    [ScriptProperty] public double Weight { get; }
    [ScriptProperty] public double Scale { get; }
}

/// <summary>An <see cref="EnvironmentValues"/> as <c>wms.environment</c> reports it. Colours are [r, g, b] in 0-1.</summary>
public sealed class EnvironmentDescriptor
{
    public EnvironmentDescriptor(EnvironmentValues values)
    {
        SunColor = Rgb(values.SunColor);
        SunDirection = [values.SunDirection.X, values.SunDirection.Y, values.SunDirection.Z];
        SunEnergy = values.SunEnergy;
        AmbientColor = Rgb(values.AmbientColor);
        AmbientEnergy = values.AmbientEnergy;
        SkyGradient = values.SkyGradient.Select(stop => new SkyGradientStopDescriptor(stop)).ToArray();
        FogEnabled = values.FogEnabled;
        FogColor = Rgb(values.FogColor);
        FogStart = values.FogStart;
        FogEnd = values.FogEnd;
        FogCurve = values.FogCurve;
        FogCurveCoefficients = values.FogCurveCoefficients.Select(c => (double)c).ToArray();
        FogCurveStart = values.FogCurveStart;
        FogCurveEnd = values.FogCurveEnd;
        HeightFogColor = Rgb(values.HeightFogColor);
        HeightFogCoefficients = values.HeightFogCoefficients.Select(c => (double)c).ToArray();
        HeightFogReferenceZ = values.HeightFogReferenceZ;
        HeightFogScale = values.HeightFogScale;
        SunGlowColor = Rgb(values.SunGlowColor);
        SunGlowCosAngle = values.SunGlowCosAngle;
        SunGlowExponent = values.SunGlowExponent;
        InteriorAmbientColor = Rgb(values.InteriorAmbientColor);
        InteriorHorizonColor = Rgb(values.InteriorHorizonColor);
        InteriorGroundColor = Rgb(values.InteriorGroundColor);
        InteriorDirectColor = Rgb(values.InteriorDirectColor);
        TerrainSpecularIntensity = values.TerrainSpecularIntensity;
        ViewDistance = values.ViewDistance;
        SkyLayers = values.SkyLayers.Select(layer => new SkyLayerDescriptor(layer)).ToArray();
        Floats = values.Floats.ToDictionary(pair => pair.Key, pair => (object?)(double)pair.Value);
        Colors = values.Colors.ToDictionary(pair => pair.Key, pair => (object?)Rgb(pair.Value));
    }

    internal static double[] Rgb(Color color) => [color.R, color.G, color.B];

    [ScriptProperty] public double[] SunColor { get; }
    [ScriptProperty] public double[] SunDirection { get; }
    [ScriptProperty] public double SunEnergy { get; }
    [ScriptProperty] public double[] AmbientColor { get; }
    [ScriptProperty] public double AmbientEnergy { get; }
    [ScriptProperty] public SkyGradientStopDescriptor[] SkyGradient { get; }
    [ScriptProperty] public bool FogEnabled { get; }
    [ScriptProperty] public double[] FogColor { get; }
    [ScriptProperty] public double FogStart { get; }
    [ScriptProperty] public double FogEnd { get; }
    [ScriptProperty] public double FogCurve { get; }
    [ScriptProperty] public double[] FogCurveCoefficients { get; }
    [ScriptProperty] public double FogCurveStart { get; }
    [ScriptProperty] public double FogCurveEnd { get; }
    [ScriptProperty] public double[] HeightFogColor { get; }
    [ScriptProperty] public double[] HeightFogCoefficients { get; }
    [ScriptProperty] public double HeightFogReferenceZ { get; }
    [ScriptProperty] public double HeightFogScale { get; }
    [ScriptProperty] public double[] SunGlowColor { get; }
    [ScriptProperty] public double SunGlowCosAngle { get; }
    [ScriptProperty] public double SunGlowExponent { get; }
    [ScriptProperty] public double[] InteriorAmbientColor { get; }
    [ScriptProperty] public double[] InteriorHorizonColor { get; }
    [ScriptProperty] public double[] InteriorGroundColor { get; }
    [ScriptProperty] public double[] InteriorDirectColor { get; }
    [ScriptProperty] public double TerrainSpecularIntensity { get; }
    [ScriptProperty] public double ViewDistance { get; }
    [ScriptProperty] public SkyLayerDescriptor[] SkyLayers { get; }

    /// <summary>Named float channels a plugin's sources contribute.</summary>
    [ScriptProperty] public IDictionary<string, object?> Floats { get; }

    /// <summary>Named colour channels a plugin's sources contribute, as [r, g, b].</summary>
    [ScriptProperty] public IDictionary<string, object?> Colors { get; }
}
