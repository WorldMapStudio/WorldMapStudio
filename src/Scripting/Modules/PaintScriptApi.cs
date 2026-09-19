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

    private PaintToolFactory Factory => _context.Tools.PaintToolFactory;

    /// <summary>The brush the Paint tool draws with.</summary>
    [ScriptProperty]
    public PaintBrushDescriptor CurrentBrush => new(Factory.Brush, Factory.Options);

    /// <summary>Changes the fields named in <paramref name="options"/> and returns the brush: the shared brush
    /// fields (<c>Radius</c>, <c>Strength</c>, <c>Hardness</c>, <c>Spacing</c>, <c>AirbrushRate</c>,
    /// <c>Spray</c>, <c>SprayCount</c>, <c>SprayScatter</c>, <c>Invert</c>; <c>Opacity</c> and <c>Erase</c> are
    /// accepted for <c>Strength</c> and <c>Invert</c>), plus <c>Color</c> as [r, g, b] in 0-1 and
    /// <c>PaintOnObject</c>.</summary>
    [ScriptFunction]
    public PaintBrushDescriptor SetBrush(object? options)
    {
        Apply(Factory.Brush, Factory.Options, options);
        return CurrentBrush;
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
        Brush brush = Factory.Brush.Clone();
        ImagePaintOptions paint = Factory.Options.Clone();
        Apply(brush, paint, options);

        var stroke = new BrushStroke(brush, new ImagePaintTarget(target, paint, _context.Landscape), _context.EditSessions);
        if (!stroke.Begin())
        {
            throw new InvalidOperationException("The entity's image component is not bound to an image.");
        }

        foreach (Vector3 point in world)
        {
            stroke.MoveTo(point);
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

    private static void Apply(Brush brush, ImagePaintOptions paint, object? options)
    {
        BrushScriptOptions.Apply(brush, options, (key, value) =>
        {
            switch (key.ToLowerInvariant())
            {
                case "paintonobject":
                    paint.PaintOnObject = BrushScriptOptions.ToBool(value);
                    return true;
                case "color":
                    double[] rgb = (value as IEnumerable)?.Cast<object?>().Select(ToDouble).ToArray() ?? [];
                    if (rgb.Length != 3)
                    {
                        throw new ArgumentException("Color must be [r, g, b] in 0-1.");
                    }

                    paint.Color = new Color((float)rgb[0], (float)rgb[1], (float)rgb[2]);
                    return true;
                default:
                    return false;
            }
        });
    }

    private static double ToDouble(object? value) => BrushScriptOptions.ToDouble(value);
}

/// <summary>The paint brush as <c>wms.paint</c> reports it: the shared brush fields plus the image options.
/// <c>Opacity</c> and <c>Erase</c> mirror <c>Strength</c> and <c>Invert</c>.</summary>
public sealed class PaintBrushDescriptor : BrushDescriptor
{
    public PaintBrushDescriptor(Brush brush, ImagePaintOptions options)
        : base(brush)
    {
        Opacity = brush.Strength;
        Erase = brush.Invert;
        Color = [options.Color.R, options.Color.G, options.Color.B];
        PaintOnObject = options.PaintOnObject;
    }

    [ScriptProperty] public double Opacity { get; }
    [ScriptProperty] public bool Erase { get; }
    [ScriptProperty] public double[] Color { get; }
    [ScriptProperty] public bool PaintOnObject { get; }
}
