using Godot;

namespace WorldMapStudio;

/// <summary>Image-specific paint settings that sit alongside the shared <see cref="Brush"/>.</summary>
public sealed class ImagePaintOptions
{
    public Color Color { get; set; } = Colors.White;

    /// <summary>Paint straight onto an object-mode image instead of projecting through the terrain.</summary>
    public bool PaintOnObject { get; set; } = true;

    public ImagePaintOptions Clone() => new() { Color = Color, PaintOnObject = PaintOnObject };
}

/// <summary>An <see cref="IStrokeTarget"/> over the image an <see cref="ImageComponent"/> places.</summary>
public sealed class ImagePaintTarget : IStrokeTarget
{
    private readonly ImageComponent _component;
    private readonly ImagePaintOptions _options;
    private readonly LandscapeSystem _landscape;

    private PaintImage? _image;

    public ImagePaintTarget(ImageComponent component, ImagePaintOptions options, LandscapeSystem landscape)
    {
        _component = component;
        _options = options;
        _landscape = landscape;
    }

    public ImageComponent Component => _component;

    public bool Covers(Vector3 world) => Contains(_component, ToLocal(world));

    /// <summary>Whether <paramref name="local"/> lies inside the placement's footprint.</summary>
    public static bool Contains(ImageComponent component, Vector3 local) =>
        Mathf.Abs(local.X) <= component.WorldSizeX * 0.5f &&
        Mathf.Abs(local.Z) <= component.WorldSizeZ * 0.5f;

    public bool Begin()
    {
        if (_component.Image is not { } image)
        {
            return false;
        }

        _image = image;
        image.BeginStroke();
        return true;
    }

    public bool Dab(in BrushDab dab)
    {
        bool changed = _component.Paint(
            ToLocal(dab.Center), dab.Radius, _options.Color, dab.Strength * dab.Pressure, dab.Invert, dab.Hardness);
        if (changed)
        {
            // A content signal the rebuilder polls on a timer, not a scene-version bump, which every
            // per-frame cache in the editor would rebuild for the length of the stroke.
            _landscape.Rebuilder.NoticePaint();
        }

        return changed;
    }

    public IEditCommand? Finish()
    {
        if (_image is not { } image)
        {
            return null;
        }

        _image = null;
        var edits = image.EndStroke();
        return edits.Count == 0
            ? null
            : new PaintImageChunksCommand(image, edits, _component.AffectedEntities, $"Paint {image.Name}");
    }

    public void Cancel()
    {
        // Drained even when not recording, so an abandoned stroke's tracking never leaks into the next.
        _image?.EndStroke();
        _image = null;
    }

    private Vector3 ToLocal(Vector3 world) =>
        _component.Owner is { } owner ? owner.Transform.AffineInverse() * world : world;
}
