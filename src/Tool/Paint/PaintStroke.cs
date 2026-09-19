using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// One brush stroke over an <see cref="ImageComponent"/>'s bound image: dabs laid along the travelled
/// path, recorded as a single undo step when the stroke finishes. Shared by <see cref="PaintTool"/> and
/// <c>wms.paint</c>. Points are in the target's local space.
/// </summary>
public sealed class PaintStroke
{
    private const float StampSpacingFraction = 0.25f;
    private const float MinStampSpacing = 0.05f;
    private const int MaxStampsPerCall = 512;

    private readonly PaintBrush _brush;
    private readonly EditSessionManager _sessions;
    private readonly LandscapeSystem _landscape;

    private ImageComponent? _target;
    private PaintImage? _image;
    private Vector3 _lastStamp;
    private bool _stamped;

    public PaintStroke(PaintBrush brush, EditSessionManager sessions, LandscapeSystem landscape)
    {
        _brush = brush;
        _sessions = sessions;
        _landscape = landscape;
    }

    public ImageComponent? Target => _target;

    public bool IsActive => _target != null;

    /// <summary>Whether the last <see cref="StampTo"/> laid at least one dab.</summary>
    public bool LaidDabs { get; private set; }

    /// <summary>Whether <paramref name="local"/> lies inside the target's footprint.</summary>
    public static bool Contains(ImageComponent target, Vector3 local) =>
        Mathf.Abs(local.X) <= target.WorldSizeX * 0.5f &&
        Mathf.Abs(local.Z) <= target.WorldSizeZ * 0.5f;

    /// <summary>Starts a stroke on <paramref name="target"/>. False when it has no bound image.</summary>
    public bool Begin(ImageComponent target)
    {
        if (_target != null)
        {
            throw new InvalidOperationException("A stroke is already in progress.");
        }

        if (target.Image is not { } image)
        {
            return false;
        }

        _target = target;
        _image = image;
        _stamped = false;
        image.BeginStroke();
        return true;
    }

    /// <summary>
    /// Lays dabs from the last stamped point up to <paramref name="local"/>, one every
    /// <see cref="StampSpacingFraction"/> of the brush radius of travel, carrying the leftover distance to
    /// the next call. The first dab of a stroke lands at <paramref name="local"/>. True when any pixel changed.
    /// </summary>
    public bool StampTo(Vector3 local)
    {
        ImageComponent target = _target ?? throw new InvalidOperationException("No stroke in progress.");
        LaidDabs = false;

        if (!_stamped)
        {
            _stamped = true;
            _lastStamp = local;
            LaidDabs = true;
            return Dab(target, local);
        }

        float spacing = Math.Max(_brush.Radius * StampSpacingFraction, MinStampSpacing);
        var travel = new Vector3(local.X - _lastStamp.X, 0.0f, local.Z - _lastStamp.Z);
        float distance = travel.Length();
        if (distance < spacing)
        {
            return false;
        }

        Vector3 step = (travel / distance) * spacing;
        int stamps = Math.Min((int)(distance / spacing), MaxStampsPerCall);
        bool changed = false;
        for (int i = 0; i < stamps; i++)
        {
            _lastStamp += step;
            changed |= Dab(target, _lastStamp);
        }

        // If the pointer outran the cap, drop the backlog rather than let it accumulate.
        if (stamps == MaxStampsPerCall)
        {
            _lastStamp = local;
        }

        LaidDabs = true;
        return changed;
    }

    /// <summary>One dab at <paramref name="local"/>, restarting travel from there.</summary>
    public bool StampAt(Vector3 local)
    {
        ImageComponent target = _target ?? throw new InvalidOperationException("No stroke in progress.");
        _stamped = true;
        _lastStamp = local;
        return Dab(target, local);
    }

    /// <summary>Ends the stroke. With <paramref name="record"/> the edit becomes one undo step; true when
    /// one was recorded.</summary>
    public bool Finish(bool record)
    {
        if (_target == null)
        {
            return false;
        }

        ImageComponent target = _target;
        PaintImage image = _image!;
        _target = null;
        _image = null;
        _stamped = false;

        // Drained even when not recording, so an abandoned stroke's tracking never leaks into the next.
        var edits = image.EndStroke();
        if (!record || edits.Count == 0)
        {
            return false;
        }

        _sessions.Record(new PaintImageChunksCommand(image, edits, target.AffectedEntities, $"Paint {image.Name}"));
        return true;
    }

    private bool Dab(ImageComponent target, Vector3 local)
    {
        bool changed = target.Paint(local, _brush.Radius, _brush.Color, _brush.Opacity, _brush.Erase);
        if (changed)
        {
            // A content signal the rebuilder polls on a timer, not a scene-version bump, which every
            // per-frame cache in the editor would rebuild for the length of the stroke.
            _landscape.Rebuilder.NoticePaint();
        }

        return changed;
    }
}
