using System;
using System.Globalization;

namespace WorldMapStudio;

/// <summary>The brush vocabulary every script API shares:
/// <c>{ radius, strength, hardness, spacing, airbrushRate, spray, sprayCount, sprayScatter, invert }</c>,
/// keys case-insensitive. <c>opacity</c> and <c>erase</c> are accepted as <c>strength</c> and
/// <c>invert</c>.</summary>
public static class BrushScriptOptions
{
    /// <summary>Applies the brush fields named in <paramref name="options"/>. A key that is not a brush
    /// field goes to <paramref name="other"/>, which returns whether it took it; without one it throws.</summary>
    public static void Apply(Brush brush, object? options, Func<string, object?, bool>? other = null)
    {
        if (ScriptJson.AsMap(options) is not { } map)
        {
            return;
        }

        foreach ((string key, object? value) in map)
        {
            if (!TryApply(brush, key, value) && !(other?.Invoke(key, value) ?? false))
            {
                throw new ArgumentException($"Unknown brush option '{key}'.");
            }
        }
    }

    public static bool TryApply(Brush brush, string key, object? value)
    {
        switch (key.ToLowerInvariant())
        {
            case "radius":
                brush.Radius = (float)ToDouble(value);
                return true;
            case "strength":
            case "opacity":
                brush.Strength = (float)ToDouble(value);
                return true;
            case "hardness":
                brush.Hardness = (float)ToDouble(value);
                return true;
            case "spacing":
                brush.Spacing = (float)ToDouble(value);
                return true;
            case "airbrushrate":
                brush.AirbrushRate = (float)ToDouble(value);
                return true;
            case "spray":
                brush.Spray = ToBool(value);
                return true;
            case "spraycount":
                brush.SprayCount = (int)ToDouble(value);
                return true;
            case "sprayscatter":
                brush.SprayScatter = (float)ToDouble(value);
                return true;
            case "invert":
            case "erase":
                brush.Invert = ToBool(value);
                return true;
            default:
                return false;
        }
    }

    public static double ToDouble(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    public static bool ToBool(object? value) => Convert.ToBoolean(value, CultureInfo.InvariantCulture);
}

/// <summary>A <see cref="Brush"/> as a script API reports it.</summary>
public class BrushDescriptor
{
    public BrushDescriptor(Brush brush)
    {
        Radius = brush.Radius;
        Strength = brush.Strength;
        Hardness = brush.Hardness;
        Spacing = brush.Spacing;
        AirbrushRate = brush.AirbrushRate;
        Spray = brush.Spray;
        SprayCount = brush.SprayCount;
        SprayScatter = brush.SprayScatter;
        Invert = brush.Invert;
    }

    [ScriptProperty] public double Radius { get; }
    [ScriptProperty] public double Strength { get; }
    [ScriptProperty] public double Hardness { get; }
    [ScriptProperty] public double Spacing { get; }
    [ScriptProperty] public double AirbrushRate { get; }
    [ScriptProperty] public bool Spray { get; }
    [ScriptProperty] public int SprayCount { get; }
    [ScriptProperty] public double SprayScatter { get; }
    [ScriptProperty] public bool Invert { get; }
}
