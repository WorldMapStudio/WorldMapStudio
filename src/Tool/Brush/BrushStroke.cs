using System;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// One brush stroke over an <see cref="IStrokeTarget"/>: dabs laid along the travelled path, an airbrush
/// repeat while the pointer is still, optional spray, and the target's undo command recorded as one step
/// when the stroke finishes. Shared by every brush tool and its script API. Points are world space.
/// </summary>
public sealed class BrushStroke
{
    private const float MinSpacingWorld = 0.05f;
    private const int MaxDabsPerCall = 512;
    private const float SpraySubRadiusFraction = 0.25f;

    private readonly Brush _brush;
    private readonly IStrokeTarget _target;
    private readonly EditSessionManager _sessions;
    private readonly Random _random;

    private Vector3 _last;
    private bool _stamped;
    private bool _invert;
    private double _lastDabTime;

    public BrushStroke(Brush brush, IStrokeTarget target, EditSessionManager sessions, Random? random = null)
    {
        _brush = brush;
        _target = target;
        _sessions = sessions;
        _random = random ?? new Random();
    }

    public IStrokeTarget Target => _target;

    public bool IsActive { get; private set; }

    /// <summary>Whether this stroke's dabs are inverted.</summary>
    public bool IsInverted => _invert;

    /// <summary>Whether the last <see cref="MoveTo"/> or <see cref="Advance"/> laid at least one dab.</summary>
    public bool LaidDabs { get; private set; }

    /// <summary>Starts the stroke. <paramref name="invert"/> flips <see cref="Brush.Invert"/> for this stroke
    /// only. False when the target cannot be edited right now.</summary>
    public bool Begin(bool invert = false)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("A stroke is already in progress.");
        }

        if (!_target.Begin())
        {
            return false;
        }

        IsActive = true;
        _stamped = false;
        _invert = _brush.Invert ^ invert;
        return true;
    }

    /// <summary>
    /// Lays dabs from the last dab up to <paramref name="world"/>, one every <see cref="Brush.Spacing"/> of
    /// the radius of travel, carrying the leftover distance to the next call. The first dab of a stroke lands
    /// at <paramref name="world"/>. Points the target does not cover are skipped. True when anything changed.
    /// </summary>
    public bool MoveTo(Vector3 world)
    {
        RequireActive();
        LaidDabs = false;
        if (!_target.Covers(world))
        {
            return false;
        }

        if (!_stamped)
        {
            _stamped = true;
            _last = world;
            LaidDabs = true;
            return Dab(world);
        }

        float spacing = Math.Max(_brush.Radius * _brush.Spacing, MinSpacingWorld);
        var travel = new Vector3(world.X - _last.X, 0.0f, world.Z - _last.Z);
        float distance = travel.Length();
        if (distance < spacing)
        {
            return false;
        }

        Vector3 step = (travel / distance) * spacing;
        int dabs = Math.Min((int)(distance / spacing), MaxDabsPerCall);
        bool changed = false;
        for (int i = 0; i < dabs; i++)
        {
            _last += step;
            _last.Y = world.Y;
            changed |= Dab(_last);
        }

        // If the pointer outran the cap, drop the backlog rather than let it accumulate.
        if (dabs == MaxDabsPerCall)
        {
            _last = world;
        }

        LaidDabs = true;
        return changed;
    }

    /// <summary>One dab at <paramref name="world"/>, restarting travel from there.</summary>
    public bool DabAt(Vector3 world)
    {
        RequireActive();
        _stamped = true;
        _last = world;
        return Dab(world);
    }

    /// <summary>
    /// <see cref="MoveTo"/> plus the airbrush: a moving pointer spaces dabs by travel so density does not ride
    /// on the frame rate, while a still one keeps dabbing at <see cref="Brush.AirbrushRate"/> so holding the
    /// brush down builds up. <paramref name="now"/> is in seconds, from any monotonic clock.
    /// </summary>
    public bool Advance(Vector3 world, double now)
    {
        bool changed = MoveTo(world);
        if (LaidDabs)
        {
            _lastDabTime = now;
        }
        else if (now - _lastDabTime >= 1.0 / _brush.AirbrushRate && _target.Covers(world))
        {
            _lastDabTime = now;
            changed |= DabAt(world);
        }

        return changed;
    }

    /// <summary>Ends the stroke. With <paramref name="record"/> the target's edit becomes one undo step; true
    /// when one was recorded.</summary>
    public bool Finish(bool record)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        _stamped = false;

        if (!record)
        {
            _target.Cancel();
            return false;
        }

        if (_target.Finish() is not { } command)
        {
            return false;
        }

        _sessions.Record(command);
        return true;
    }

    private bool Dab(Vector3 center)
    {
        float radius = _brush.Radius;
        if (!_brush.Spray)
        {
            return _target.Dab(new BrushDab(center, radius, _brush.Strength, _invert, _brush.Hardness));
        }

        float subRadius = radius * SpraySubRadiusFraction;
        float reach = Math.Max(radius - subRadius, 0.0f) * _brush.SprayScatter;
        bool changed = false;
        for (int i = 0; i < _brush.SprayCount; i++)
        {
            float angle = _random.NextSingle() * Mathf.Tau;
            float distance = reach * MathF.Sqrt(_random.NextSingle());
            var position = new Vector3(
                center.X + (MathF.Cos(angle) * distance),
                center.Y,
                center.Z + (MathF.Sin(angle) * distance));
            changed |= _target.Dab(new BrushDab(position, subRadius, _brush.Strength, _invert, _brush.Hardness));
        }

        return changed;
    }

    private void RequireActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("No stroke in progress.");
        }
    }
}
