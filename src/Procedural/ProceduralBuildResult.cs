using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// One output a procedural function may fill: model geometry (surfaces/references written into a
/// format-carrying <see cref="ModelAsset"/>). Declared statically by
/// <see cref="IProceduralFunction.Outputs"/> so the inspector can draw a format combo and material-slot
/// bindings before anything is built. A function that only paints (see <see cref="ProceduralPaint"/>)
/// declares none.
/// </summary>
public sealed record ProceduralOutputSlot(
    string Name,
    string DisplayName,
    IReadOnlyList<string> SupportedFormats,
    IReadOnlyList<MeshMaterialSlot> MaterialSlots,
    string Description = "");

/// <summary>One model a build produced, placed at <see cref="Transform"/> relative to the component.</summary>
public sealed record ProceduralModelOutput(ProceduralOutputSlot Slot, Transform3D Transform, ModelAsset Asset);

/// <summary>
/// One "paint a mask" primitive: a disc when <see cref="A"/> equals <see cref="B"/>, a capsule-shaped
/// stroke along the segment otherwise. The vocabulary a build's landscape contribution is restricted
/// to, so chunk-independence and pure scatter hold by construction rather than by every function
/// author's discipline.
///
/// <see cref="Value"/> only matters when the channel this stroke lands on carries more than one
/// component — see <see cref="ProceduralPaintRasterizer"/>. Defaults to white so an unmodified caller
/// painting a scalar mask channel needs no change: white composited by the scalar path's own max is
/// simply never read.
/// </summary>
public readonly record struct ProceduralStroke(string Channel, Vector3 A, Vector3 B, float Radius, float Falloff, Color Value);

/// <summary>
/// A build's landscape contribution: a flat list of strokes, scattered into named channels by
/// <see cref="ProceduralComponent.Rasterize"/>. Empty for a build that only emits model geometry.
/// </summary>
public sealed class ProceduralPaint
{
    public static readonly ProceduralPaint Empty = new([]);

    public IReadOnlyList<ProceduralStroke> Strokes { get; }

    /// <summary>Flat (Y = 0) bounds of every stroke, already grown by its own radius.</summary>
    public Aabb LocalBounds { get; }

    /// <summary>Distinct channel names strokes write to, for problem reporting.</summary>
    public IReadOnlyList<string> Channels { get; }

    public ProceduralPaint(IReadOnlyList<ProceduralStroke> strokes)
    {
        Strokes = strokes;
        Channels = strokes.Select(stroke => stroke.Channel).Distinct(StringComparer.Ordinal).ToList();
        LocalBounds = ComputeBounds(strokes);
    }

    private static Aabb ComputeBounds(IReadOnlyList<ProceduralStroke> strokes)
    {
        if (strokes.Count == 0)
        {
            return new Aabb();
        }

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (ProceduralStroke stroke in strokes)
        {
            minX = Mathf.Min(minX, Mathf.Min(stroke.A.X, stroke.B.X) - stroke.Radius);
            maxX = Mathf.Max(maxX, Mathf.Max(stroke.A.X, stroke.B.X) + stroke.Radius);
            minZ = Mathf.Min(minZ, Mathf.Min(stroke.A.Z, stroke.B.Z) - stroke.Radius);
            maxZ = Mathf.Max(maxZ, Mathf.Max(stroke.A.Z, stroke.B.Z) + stroke.Radius);
        }

        return new Aabb(new Vector3(minX, 0.0f, minZ), new Vector3(maxX - minX, 0.0f, maxZ - minZ));
    }
}

/// <summary>
/// What one <see cref="ProceduralSystem.Build"/> call produced: any number of placed models plus a
/// landscape paint contribution. One result, two consumers — <see cref="ProceduralComponent.BuildNode"/>
/// instantiates the models, <see cref="ProceduralComponent.Rasterize"/> scatters the paint.
/// </summary>
public sealed class ProceduralBuildResult
{
    public static readonly ProceduralBuildResult Empty = new([], ProceduralPaint.Empty);

    public IReadOnlyList<ProceduralModelOutput> Models { get; }

    public ProceduralPaint Paint { get; }

    /// <summary>
    /// Union of every model output's transformed bounds, merged with the paint's bounds grown to the
    /// landscape's nominal height extent — a paint-only model sits at Y = 0 but paints terrain at any
    /// height, the same reasoning a road's flat authoring uses (see <c>builtin.procedural.road</c>).
    /// </summary>
    public Aabb LocalBounds { get; }

    public ProceduralBuildResult(IReadOnlyList<ProceduralModelOutput> models, ProceduralPaint paint)
    {
        Models = models;
        Paint = paint;
        LocalBounds = ComputeBounds(models, paint);
    }

    /// <summary>Combines every model output into one <see cref="ModelAsset"/> for previewing —
    /// e.g. the model picker, which shows one thumbnail per model regardless of how many outputs it
    /// declares. Ignores <see cref="Paint"/>: a preview only ever shows geometry.</summary>
    public ModelAsset ToPreviewAsset()
    {
        var parts = new List<ModelPart>();
        foreach (ProceduralModelOutput output in Models)
        {
            foreach (ModelPart part in output.Asset.Parts)
            {
                parts.Add(part with { Transform = output.Transform * part.Transform });
            }
        }

        return new ModelAsset("", parts);
    }

    private static Aabb ComputeBounds(IReadOnlyList<ProceduralModelOutput> models, ProceduralPaint paint)
    {
        var boxes = new List<Aabb>(models.Count + 1);
        foreach (ProceduralModelOutput output in models)
        {
            boxes.Add(output.Transform * output.Asset.LocalBounds);
        }

        if (paint.Strokes.Count > 0)
        {
            Aabb flat = paint.LocalBounds;
            boxes.Add(new Aabb(
                new Vector3(flat.Position.X, -LandscapeGrid.NominalHeightExtent, flat.Position.Z),
                new Vector3(flat.Size.X, LandscapeGrid.NominalHeightExtent * 2.0f, flat.Size.Z)));
        }

        if (boxes.Count == 0)
        {
            return new Aabb();
        }

        Aabb combined = boxes[0];
        for (int i = 1; i < boxes.Count; i++)
        {
            combined = combined.Merge(boxes[i]);
        }

        return combined;
    }
}
