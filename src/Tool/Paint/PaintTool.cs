using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Projects a circular brush onto the selected entity's bound <see cref="PaintImage"/>, via
/// its <see cref="ImageComponent"/> placement.</summary>
public sealed class PaintTool : ITool
{
    private readonly SelectionSystem _selection;
    private readonly SceneEntityRegistry _scene;
    private readonly LandscapeSystem _landscape;
    private readonly TerrainProbe _terrain;
    private readonly PaintBrush _brush;
    private readonly PaintStroke _stroke;

    // When the stroke last laid a dab. A moving pointer spaces dabs by travel so stroke density does
    // not ride on the frame rate; a still pointer keeps dabbing at a fixed wall-clock rate so holding
    // the brush down still builds paint up like an airbrush.
    private ulong _lastStampMs;

    private const ulong StationaryStampIntervalMs = 16;

    public PaintTool(ToolContext context, PaintBrush brush)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _landscape = context.Editor.Landscape;
        _terrain = new TerrainProbe(_landscape);
        _brush = brush;
        _stroke = new PaintStroke(brush, context.Sessions, _landscape);
    }

    public string Name => "Paint";

    public bool CapturesMouse => _stroke.IsActive;

    public void DrawToolbar()
    {
        float radius = _brush.Radius;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragFloat("Radius", ref radius, 0.1f, PaintBrush.MinRadius, PaintBrush.MaxRadius))
        {
            _brush.Radius = radius;
        }

        ImGui.SameLine();

        float opacity = _brush.Opacity;
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragFloat("Opacity", ref opacity, 0.01f, PaintBrush.MinOpacity, PaintBrush.MaxOpacity))
        {
            _brush.Opacity = opacity;
        }

        ImGui.SameLine();

        bool erase = _brush.Erase;
        if (ImGui.Checkbox("Erase", ref erase))
        {
            _brush.Erase = erase;
        }

        ImGui.SameLine();

        bool colorAvailable = (ActiveTarget()?.Image?.Components ?? 1) > 1;
        if (!colorAvailable)
        {
            ImGui.BeginDisabled();
        }

        ImGui.SetNextItemWidth(160.0f);
        var colorValue = new NVector3(_brush.Color.R, _brush.Color.G, _brush.Color.B);
        if (ImGui.ColorEdit3("Color", ref colorValue))
        {
            _brush.Color = new Color(colorValue.X, colorValue.Y, colorValue.Z);
        }

        if (!colorAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        bool objectAvailable = ActiveTarget()?.DisplayLayer?.DisplayMode == ImageDisplayMode.Object;
        if (!objectAvailable)
        {
            ImGui.BeginDisabled();
        }

        bool paintOnObject = _brush.PaintOnObject;
        if (ImGui.Checkbox("Paint on Object", ref paintOnObject))
        {
            _brush.PaintOnObject = paintOnObject;
        }

        if (!objectAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(ActiveTarget()?.Owner?.DisplayName ?? "No image selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        ImageComponent? target = ActiveTarget();
        if (target == null || context.CameraFlying)
        {
            _stroke.Finish(record: false);
            return;
        }

        bool onObject = _brush.PaintOnObject && target.DisplayLayer?.DisplayMode == ImageDisplayMode.Object;

        var projector = new ViewportProjector(context.Camera, context.ImageMin, context.ImageSize);
        DrawTargetOutline(target, onObject, projector);

        bool hit = TryHit(target, onObject, context, out GVector3 local);
        if (hit)
        {
            DrawBrush(target, onObject, local, projector);
        }

        if (!_stroke.IsActive && context.Hovered && hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _stroke.Begin(target);
        }

        if (_stroke.IsActive)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _stroke.Finish(record: true);
                return;
            }

            if (_stroke.Target == target && hit)
            {
                Stamp(local);
            }
        }
    }

    private void Stamp(GVector3 local)
    {
        _stroke.StampTo(local);
        ulong now = Time.GetTicksMsec();
        if (_stroke.LaidDabs)
        {
            _lastStampMs = now;
        }
        else if (now - _lastStampMs >= StationaryStampIntervalMs)
        {
            _lastStampMs = now;
            _stroke.StampAt(local);
        }
    }

    public void Deactivate()
    {
        _stroke.Finish(record: true);
    }

    private ImageComponent? ActiveTarget() =>
        _selection.Selected.OfType<SceneEntity>()
            .Where(entity => _scene.Contains(entity))
            .Select(entity => entity.Component<ImageComponent>())
            .FirstOrDefault(component => component != null);

    private bool TryHit(ImageComponent target, bool onObject, in ViewportContext context, out GVector3 local)
    {
        local = default;
        if (!context.Hovered)
        {
            return false;
        }

        GVector3 rayOrigin = context.PointerRayOrigin;
        GVector3 rayDir = context.PointerRayDir;

        if (onObject)
        {
            return TryHitObject(target, rayOrigin, rayDir, out local);
        }

        // The viewport already cast this ray against the terrain for its pointer; reuse the hit
        // rather than casting it a second time in the same frame.
        if (context.TerrainHit && TryProjectOntoTarget(target, context.TerrainPoint, out local))
        {
            return true;
        }

        return TryHitFallbackPlane(target, rayOrigin, rayDir, out local);
    }

    /// <summary>Object display mode paints directly onto its own surface instead of projecting through
    /// the terrain. That surface is a backdrop plus one quad per resident chunk, every one of them
    /// coplanar at the placement's local y=0 (see <see cref="ImageComponent.BuildNode"/>), and the
    /// backdrop spans the whole footprint — so intersecting that one plane gives exactly what a
    /// ray-vs-triangle sweep of every quad gives, without a per-frame walk of the node tree and the
    /// marshalled child list, transform and AABB fetch each node costs.</summary>
    private static bool TryHitObject(ImageComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        local = default;

        Transform3D inverse = target.Owner!.Transform.AffineInverse();
        GVector3 origin = inverse * rayOrigin;
        GVector3 direction = inverse.Basis * rayDir;
        if (Mathf.Abs(direction.Y) < 1e-6f)
        {
            return false;
        }

        float t = -origin.Y / direction.Y;
        if (t < 0.0f)
        {
            return false;
        }

        GVector3 candidate = origin + (direction * t);
        if (!PaintStroke.Contains(target, candidate))
        {
            return false;
        }

        local = candidate;
        return true;
    }

    private static bool TryProjectOntoTarget(ImageComponent target, GVector3 world, out GVector3 local)
    {
        local = default;
        GVector3 candidate = target.Owner!.Transform.AffineInverse() * world;
        if (!PaintStroke.Contains(target, candidate))
        {
            return false;
        }

        local = candidate;
        return true;
    }

    private static bool TryHitFallbackPlane(ImageComponent target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        if (Mathf.Abs(rayDir.Y) < 1e-6f)
        {
            local = default;
            return false;
        }

        float t = -rayOrigin.Y / rayDir.Y;
        if (t < 0.0f)
        {
            local = default;
            return false;
        }

        local = target.Owner!.Transform.AffineInverse() * (rayOrigin + (rayDir * t));
        return PaintStroke.Contains(target, local);
    }

    /// <summary>Where a local footprint point actually lands for display — the object's own flat
    /// surface when painting targets it, or dropped onto the terrain otherwise. Must match whatever
    /// <see cref="TryHit"/> just hit-tested against, or the outline/brush would lie about where a
    /// click actually lands.</summary>
    private GVector3 SurfacePoint(ImageComponent target, GVector3 local, bool onObject)
    {
        GVector3 world = target.Owner!.Transform * new GVector3(local.X, 0.0f, local.Z);
        return onObject ? world : _terrain.DropToHeight(world);
    }

    private void DrawTargetOutline(ImageComponent target, bool onObject, in ViewportProjector projector)
    {
        GVector3 half = new(target.WorldSizeX * 0.5f, 0.0f, target.WorldSizeZ * 0.5f);
        GVector3[] local =
        [
            new GVector3(-half.X, 0.0f, -half.Z),
            new GVector3(half.X, 0.0f, -half.Z),
            new GVector3(half.X, 0.0f, half.Z),
            new GVector3(-half.X, 0.0f, half.Z),
        ];

        Span<NVector2> screen = stackalloc NVector2[4];
        for (int i = 0; i < local.Length; i++)
        {
            if (!projector.TryProject(SurfacePoint(target, local[i], onObject), out screen[i]))
            {
                return;
            }
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(new NVector4(1.0f, 0.62f, 0.20f, 0.95f));
        for (int i = 0; i < screen.Length; i++)
        {
            drawList.AddLine(screen[i], screen[(i + 1) % screen.Length], color, 2.0f);
        }
    }

    private void DrawBrush(ImageComponent target, bool onObject, GVector3 local, in ViewportProjector projector)
    {
        const int Segments = 48;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(_brush.Erase
            ? new NVector4(1.0f, 0.35f, 0.25f, 0.95f)
            : new NVector4(0.2f, 0.75f, 1.0f, 0.95f));

        NVector2? previous = null;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            GVector3 point = new(
                local.X + (Mathf.Cos(angle) * _brush.Radius),
                0.0f,
                local.Z + (Mathf.Sin(angle) * _brush.Radius));
            GVector3 world = SurfacePoint(target, point, onObject);
            if (!projector.TryProject(world, out NVector2 screen))
            {
                previous = null;
                continue;
            }

            if (previous is { } p)
            {
                drawList.AddLine(p, screen, color, 2.0f);
            }

            previous = screen;
        }
    }
}
