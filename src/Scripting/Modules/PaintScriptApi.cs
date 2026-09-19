using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The paint brush and paint strokes, exposed to JS as <c>wms.paint</c>. Sculpting is painting an image
/// bound as a height deformer, so this covers both.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class PaintScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "paint";

    public PaintScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private PaintBrush Brush => _context.Tools.PaintToolFactory.Brush;

    /// <summary>The brush the Paint tool draws with.</summary>
    [ScriptProperty]
    public PaintBrushDescriptor CurrentBrush => Describe(Brush);

    /// <summary>Changes the fields named in <paramref name="options"/> (<c>Radius</c>, <c>Opacity</c>,
    /// <c>Color</c> as [r, g, b] in 0-1, <c>Erase</c>, <c>PaintOnObject</c>) and returns the brush.</summary>
    [ScriptFunction]
    public PaintBrushDescriptor SetBrush(object? options)
    {
        Apply(Brush, options);
        return Describe(Brush);
    }

    /// <summary>
    /// Paints along <paramref name="points"/>, [x, z] or [x, y, z] in world space, onto the image bound to
    /// the entity's image component, as one undo step. <paramref name="options"/> overrides brush fields for
    /// this call only. Points outside the image's footprint are skipped. True when any pixel changed.
    /// </summary>
    [ScriptFunction]
    public bool Stroke(ScriptEntityHandle handle, object? points, object? options = null)
    {
        (SceneEntity entity, ImageComponent target) = RequireTarget(handle);
        List<Vector3> world = ParsePoints(points, entity.Transform.Origin.Y);
        return Run(entity, target, world, options);
    }

    /// <summary>A single dab at world [x, z], as one undo step.</summary>
    [ScriptFunction]
    public bool Dab(ScriptEntityHandle handle, double x, double z, object? options = null)
    {
        (SceneEntity entity, ImageComponent target) = RequireTarget(handle);
        var point = new Vector3((float)x, entity.Transform.Origin.Y, (float)z);
        return Run(entity, target, [point], options);
    }

    private bool Run(SceneEntity entity, ImageComponent target, List<Vector3> world, object? options)
    {
        PaintBrush brush = Brush.Clone();
        Apply(brush, options);

        var stroke = new PaintStroke(brush, _context.EditSessions, _context.Landscape);
        if (!stroke.Begin(target))
        {
            throw new InvalidOperationException("The entity's image component is not bound to an image.");
        }

        Transform3D inverse = entity.Transform.AffineInverse();
        foreach (Vector3 point in world)
        {
            Vector3 local = inverse * point;
            if (PaintStroke.Contains(target, local))
            {
                stroke.StampTo(local);
            }
        }

        return stroke.Finish(record: true);
    }

    private static (SceneEntity Entity, ImageComponent Target) RequireTarget(ScriptEntityHandle handle) =>
        handle.Resolve() is SceneEntity entity && entity.Component<ImageComponent>() is { } target
            ? (entity, target)
            : throw new InvalidOperationException("That entity has no image component.");

    private static List<Vector3> ParsePoints(object? points, float defaultY)
    {
        if (points is not IEnumerable items || points is string)
        {
            throw new ArgumentException("Points must be an array of [x, z] or [x, y, z].");
        }

        var result = new List<Vector3>();
        foreach (object? item in items)
        {
            double[] values = item is IEnumerable pair && item is not string
                ? pair.Cast<object?>().Select(ToDouble).ToArray()
                : [];
            result.Add(values.Length switch
            {
                2 => new Vector3((float)values[0], defaultY, (float)values[1]),
                3 => new Vector3((float)values[0], (float)values[1], (float)values[2]),
                _ => throw new ArgumentException("Each point must be [x, z] or [x, y, z]."),
            });
        }

        return result;
    }

    private static void Apply(PaintBrush brush, object? options)
    {
        if (ScriptJson.AsMap(options) is not { } map)
        {
            return;
        }

        foreach ((string key, object? value) in map)
        {
            switch (key.ToLowerInvariant())
            {
                case "radius":
                    brush.Radius = (float)ToDouble(value);
                    break;
                case "opacity":
                    brush.Opacity = (float)ToDouble(value);
                    break;
                case "erase":
                    brush.Erase = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case "paintonobject":
                    brush.PaintOnObject = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case "color":
                    double[] rgb = (value as IEnumerable)?.Cast<object?>().Select(ToDouble).ToArray() ?? [];
                    if (rgb.Length != 3)
                    {
                        throw new ArgumentException("Color must be [r, g, b] in 0-1.");
                    }

                    brush.Color = new Color((float)rgb[0], (float)rgb[1], (float)rgb[2]);
                    break;
                default:
                    throw new ArgumentException($"Unknown brush option '{key}'.");
            }
        }
    }

    private static double ToDouble(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static PaintBrushDescriptor Describe(PaintBrush brush) => new(brush);
}

/// <summary>The paint brush as <c>wms.paint</c> reports it.</summary>
public sealed class PaintBrushDescriptor
{
    public PaintBrushDescriptor(PaintBrush brush)
    {
        Radius = brush.Radius;
        Opacity = brush.Opacity;
        Color = [brush.Color.R, brush.Color.G, brush.Color.B];
        Erase = brush.Erase;
        PaintOnObject = brush.PaintOnObject;
    }

    [ScriptProperty] public double Radius { get; }
    [ScriptProperty] public double Opacity { get; }
    [ScriptProperty] public double[] Color { get; }
    [ScriptProperty] public bool Erase { get; }
    [ScriptProperty] public bool PaintOnObject { get; }
}
