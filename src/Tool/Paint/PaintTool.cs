using System;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>Projects a circular brush onto the selected <see cref="DrawingTargetEntity"/>.</summary>
public sealed class PaintTool : ITool
{
    private readonly SelectionSystem _selection;
    private readonly SceneEntityRegistry _scene;
    private readonly EditSessionManager _sessions;

    private float _radius = 4.0f;
    private float _opacity = 0.35f;
    private bool _erase;
    private bool _painting;
    private DrawingTargetEntity? _strokeTarget;
    private byte[]? _strokeBefore;

    public PaintTool(ToolContext context)
    {
        _selection = context.Selection;
        _scene = context.Scene;
        _sessions = context.Sessions;
    }

    public string Name => "Paint";

    public bool CapturesMouse => _painting;

    public void DrawToolbar()
    {
        ImGui.SetNextItemWidth(120.0f);
        ImGui.DragFloat("Radius", ref _radius, 0.1f, 0.1f, 512.0f);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(120.0f);
        ImGui.DragFloat("Opacity", ref _opacity, 0.01f, 0.01f, 1.0f);
        ImGui.SameLine();

        ImGui.Checkbox("Erase", ref _erase);
        ImGui.SameLine();
        ImGui.TextDisabled(ActiveTarget()?.DisplayName ?? "No drawing target selected");
    }

    public void UpdateViewport(in ViewportContext context)
    {
        DrawingTargetEntity? target = ActiveTarget();
        if (target == null || context.CameraFlying)
        {
            FinishStroke(record: false);
            return;
        }

        DrawTargetOutline(target, context.Camera, context.ImageMin);

        bool hit = TryHit(target, context, out GVector3 local);
        if (hit)
        {
            DrawBrush(target, local, context.Camera, context.ImageMin);
        }

        if (!_painting && context.Hovered && hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _painting = true;
            _strokeTarget = target;
            _strokeBefore = target.CopyPixels();
        }

        if (_painting)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                FinishStroke(record: true);
                return;
            }

            if (_strokeTarget == target && hit && target.Paint(local, _radius, _opacity, _erase))
            {
                _scene.Touch(target);
            }
        }
    }

    public void Deactivate()
    {
        FinishStroke(record: true);
    }

    private DrawingTargetEntity? ActiveTarget() =>
        _selection.Selected.OfType<DrawingTargetEntity>().FirstOrDefault(entity => _scene.Contains(entity));

    private void FinishStroke(bool record)
    {
        if (!_painting)
        {
            return;
        }

        _painting = false;
        DrawingTargetEntity? target = _strokeTarget;
        byte[]? before = _strokeBefore;
        _strokeTarget = null;
        _strokeBefore = null;

        if (!record || target == null || before == null)
        {
            return;
        }

        byte[] after = target.CopyPixels();
        if (!before.SequenceEqual(after))
        {
            _sessions.Record(new SetDrawingTargetPixelsCommand(target, before, after));
        }
    }

    private bool TryHit(DrawingTargetEntity target, in ViewportContext context, out GVector3 local)
    {
        NVector2 mouse = ImGui.GetMousePos();
        GVector2 viewport = new(mouse.X - context.ImageMin.X, mouse.Y - context.ImageMin.Y);
        if (viewport.X < 0.0f || viewport.Y < 0.0f || viewport.X > context.ImageSize.X || viewport.Y > context.ImageSize.Y)
        {
            local = default;
            return false;
        }

        GVector3 rayOrigin = context.Camera.ProjectRayOrigin(viewport);
        GVector3 rayDir = context.Camera.ProjectRayNormal(viewport);

        if (TryHitTerrain(target, rayOrigin, rayDir, out local))
        {
            return true;
        }

        return TryHitFallbackPlane(target, rayOrigin, rayDir, out local);
    }

    private bool TryHitTerrain(DrawingTargetEntity target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
    {
        local = default;
        float bestT = float.PositiveInfinity;
        bool hit = false;

        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            if (!TryHitChunk(chunk, rayOrigin, rayDir, out float t, out GVector3 world) || t >= bestT)
            {
                continue;
            }

            GVector3 candidate = target.Transform.AffineInverse() * world;
            if (!TargetContains(target, candidate))
            {
                continue;
            }

            bestT = t;
            local = candidate;
            hit = true;
        }

        return hit;
    }

    private static bool TryHitFallbackPlane(DrawingTargetEntity target, GVector3 rayOrigin, GVector3 rayDir, out GVector3 local)
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

        local = target.Transform.AffineInverse() * (rayOrigin + (rayDir * t));
        return TargetContains(target, local);
    }

    private static bool TargetContains(DrawingTargetEntity target, GVector3 local) =>
        Mathf.Abs(local.X) <= target.WorldSizeX * 0.5f &&
        Mathf.Abs(local.Z) <= target.WorldSizeZ * 0.5f;

    private static bool TryHitChunk(LandscapeChunk chunk, GVector3 rayOrigin, GVector3 rayDir, out float bestT, out GVector3 bestWorld)
    {
        bestT = float.PositiveInfinity;
        bestWorld = default;

        Transform3D inverse = chunk.Transform.AffineInverse();
        GVector3 origin = inverse * rayOrigin;
        GVector3 dir = inverse.Basis * rayDir;
        if (!TryRayBox(origin, dir, chunk.LocalBounds, out _))
        {
            return false;
        }

        LandscapeChunkOutput output = chunk.Output;
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float size = chunk.LocalBounds.Size.X;
        float step = size / quads;

        for (int z = 0; z < quads; z++)
        {
            for (int x = 0; x < quads; x++)
            {
                GVector3 topLeft = Vertex(output, x, z, step);
                GVector3 topRight = Vertex(output, x + 1, z, step);
                GVector3 bottomLeft = Vertex(output, x, z + 1, step);
                GVector3 bottomRight = Vertex(output, x + 1, z + 1, step);

                if (TryRayTriangle(origin, dir, topLeft, topRight, bottomLeft, out float t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = chunk.Transform * (origin + (dir * t));
                }

                if (TryRayTriangle(origin, dir, topRight, bottomRight, bottomLeft, out t) && t < bestT)
                {
                    bestT = t;
                    bestWorld = chunk.Transform * (origin + (dir * t));
                }
            }
        }

        return bestT < float.PositiveInfinity;
    }

    private static GVector3 Vertex(LandscapeChunkOutput output, int x, int z, float step) =>
        new(x * step, output.HeightAt(x, z), z * step);

    private static bool TryRayBox(GVector3 origin, GVector3 dir, Aabb bounds, out float tHit)
    {
        tHit = 0.0f;
        GVector3 lo = bounds.Position;
        GVector3 hi = bounds.End;

        float[] oc = [origin.X, origin.Y, origin.Z];
        float[] dc = [dir.X, dir.Y, dir.Z];
        float[] loc = [lo.X, lo.Y, lo.Z];
        float[] hic = [hi.X, hi.Y, hi.Z];

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(dc[a]) < 1e-8f)
            {
                if (oc[a] < loc[a] || oc[a] > hic[a])
                {
                    return false;
                }

                continue;
            }

            float t1 = (loc[a] - oc[a]) / dc[a];
            float t2 = (hic[a] - oc[a]) / dc[a];
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        if (tMax < 0.0f)
        {
            return false;
        }

        tHit = tMin >= 0.0f ? tMin : tMax;
        return true;
    }

    private static bool TryRayTriangle(GVector3 origin, GVector3 dir, GVector3 a, GVector3 b, GVector3 c, out float t)
    {
        const float Epsilon = 1e-6f;
        t = 0.0f;

        GVector3 edge1 = b - a;
        GVector3 edge2 = c - a;
        GVector3 h = dir.Cross(edge2);
        float det = edge1.Dot(h);
        if (Mathf.Abs(det) < Epsilon)
        {
            return false;
        }

        float invDet = 1.0f / det;
        GVector3 s = origin - a;
        float u = invDet * s.Dot(h);
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        GVector3 q = s.Cross(edge1);
        float v = invDet * dir.Dot(q);
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        t = invDet * edge2.Dot(q);
        return t >= 0.0f;
    }

    private bool TrySampleTerrainHeight(float worldX, float worldZ, out float height)
    {
        foreach (LandscapeChunk chunk in _scene.Entities.OfType<LandscapeChunk>())
        {
            GVector3 local = chunk.Transform.AffineInverse() * new GVector3(worldX, 0.0f, worldZ);
            Aabb bounds = chunk.LocalBounds;
            if (local.X < bounds.Position.X || local.Z < bounds.Position.Z ||
                local.X > bounds.End.X || local.Z > bounds.End.Z)
            {
                continue;
            }

            height = SampleChunkHeight(chunk.Output, bounds.Size.X, local.X, local.Z);
            return true;
        }

        height = 0.0f;
        return false;
    }

    private static float SampleChunkHeight(LandscapeChunkOutput output, float chunkSize, float x, float z)
    {
        int resolution = output.HeightResolution;
        int quads = resolution - 1;
        float gx = Mathf.Clamp(x / chunkSize * quads, 0.0f, quads);
        float gz = Mathf.Clamp(z / chunkSize * quads, 0.0f, quads);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, resolution - 1);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int z1 = Mathf.Min(z0 + 1, resolution - 1);
        float tx = gx - x0;
        float tz = gz - z0;

        float a = Mathf.Lerp(output.HeightAt(x0, z0), output.HeightAt(x1, z0), tx);
        float b = Mathf.Lerp(output.HeightAt(x0, z1), output.HeightAt(x1, z1), tx);
        return Mathf.Lerp(a, b, tz);
    }

    private GVector3 TerrainPoint(DrawingTargetEntity target, GVector3 local)
    {
        GVector3 world = target.Transform * new GVector3(local.X, 0.0f, local.Z);
        if (TrySampleTerrainHeight(world.X, world.Z, out float height))
        {
            world.Y = height;
        }

        return world;
    }

    private void DrawTargetOutline(DrawingTargetEntity target, Camera3D camera, NVector2 imageMin)
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
            if (!ObjectSelection.WorldToScreen(camera, TerrainPoint(target, local[i]), imageMin, out screen[i]))
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

    private void DrawBrush(DrawingTargetEntity target, GVector3 local, Camera3D camera, NVector2 imageMin)
    {
        const int Segments = 48;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(_erase
            ? new NVector4(1.0f, 0.35f, 0.25f, 0.95f)
            : new NVector4(0.2f, 0.75f, 1.0f, 0.95f));

        NVector2? previous = null;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            GVector3 point = new(
                local.X + (Mathf.Cos(angle) * _radius),
                0.0f,
                local.Z + (Mathf.Sin(angle) * _radius));
            GVector3 world = TerrainPoint(target, point);
            if (!ObjectSelection.WorldToScreen(camera, world, imageMin, out NVector2 screen))
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
