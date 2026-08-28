using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

public enum ProceduralMeshSelectionMode
{
    Vertex,
    Edge,
}

public enum ProceduralMeshModalMode
{
    None,
    Translate,
    Rotate,
}

[Subsystem(nameof(ToolWindow))]
public sealed class ProceduralMeshToolFactory : IToolFactory
{
    public float Priority => 20f;

    public string Name => "Procedural Mesh";

    public ProceduralMeshToolFactory(ToolWindow window)
    {
    }

    public ITool Create(ToolContext context) => new ProceduralMeshTool(context);
}

public sealed class ProceduralMeshTool : ITool
{
    private const float MarqueeThreshold = 5.0f;
    private const float VertexPickRadius = 9.0f;
    private const float EdgePickRadius = 6.0f;

    private readonly ToolContext _context;
    private readonly TransformGizmo _gizmo = new();
    private readonly HashSet<int> _vertices = [];
    private readonly HashSet<int> _edges = [];
    private ProceduralMeshSelectionMode _mode;

    private bool _dragging;
    private Transform3D _dragStartPivot;
    private readonly Dictionary<int, GVector3> _dragStartPositions = [];
    private bool _mouseDown;
    private bool _marquee;
    private NVector2 _marqueeStart;
    private ProceduralMeshModalMode _modalMode;
    private int _modalAxis = -1;
    private NVector2 _modalStartMouse;
    private Transform3D _modalStartPivot;
    private ProceduralMeshNetwork? _modalBefore;
    private readonly Dictionary<int, GVector3> _modalStartPositions = [];
    private string _modalDescription = "";

    public ProceduralMeshTool(ToolContext context)
    {
        _context = context;
        _gizmo.Axes = context.Axes;
        _gizmo.LocalSpace = false;
    }

    public string Name => "Procedural Mesh";

    public bool CapturesMouse => _gizmo.IsUsing || _mouseDown;

    public void DrawToolbar()
    {
        if (ImGui.RadioButton("Vertex", _mode == ProceduralMeshSelectionMode.Vertex))
        {
            _mode = ProceduralMeshSelectionMode.Vertex;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Edge", _mode == ProceduralMeshSelectionMode.Edge))
        {
            _mode = ProceduralMeshSelectionMode.Edge;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled($"{_vertices.Count} vertices, {_edges.Count} edges");
    }

    public void UpdateViewport(in ViewportContext viewport)
    {
        if (Active() is not { } active || viewport.CameraFlying)
        {
            _vertices.Clear();
            _edges.Clear();
            return;
        }

        ProceduralMeshComponent component = active.Component;
        DrawNetwork(active, viewport.Camera, viewport.ImageMin);
        if (_modalMode != ProceduralMeshModalMode.None)
        {
            UpdateModal(component, active.Entity, viewport);
            return;
        }

        HandleKeys(component);
        DriveGizmo(component, active.Entity, viewport);
        HandlePointer(component, active.Entity, viewport.Hovered && !_gizmo.IsUsing && !_gizmo.IsHovered, viewport);
    }

    private (SceneEntity Entity, ProceduralMeshComponent Component)? Active()
    {
        SceneEntity? entity = _context.Selection.Selected.OfType<SceneEntity>()
            .FirstOrDefault(entity => _context.Scene.Contains(entity) && entity.Component<ProceduralMeshComponent>() != null);
        return entity == null ? null : (entity, entity.Component<ProceduralMeshComponent>()!);
    }

    private void HandleKeys(ProceduralMeshComponent component)
    {
        if (Active() is not { } active)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.G, false) && _vertices.Count > 0)
        {
            BeginModal(component, active.Entity, ProceduralMeshModalMode.Translate, component.Network.Clone(), "Move procedural mesh vertices");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.R, false) && _vertices.Count > 0)
        {
            BeginModal(component, active.Entity, ProceduralMeshModalMode.Rotate, component.Network.Clone(), "Rotate procedural mesh vertices");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
        {
            Mutate(component, "Delete procedural mesh selection", network =>
            {
                foreach (int edgeId in _edges.ToArray())
                {
                    network.RemoveEdge(edgeId);
                }

                foreach (int vertexId in _vertices.ToArray())
                {
                    network.RemoveVertex(vertexId);
                }

                _edges.Clear();
                _vertices.Clear();
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.F, false) && _vertices.Count == 2)
        {
            Mutate(component, "Connect procedural mesh vertices", network =>
            {
                int[] ids = _vertices.ToArray();
                int? edge = network.AddEdge(ids[0], ids[1]);
                _edges.Clear();
                if (edge is int id)
                {
                    _edges.Add(id);
                }
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.S, false) && _edges.Count > 0)
        {
            Mutate(component, "Split procedural mesh edges", network =>
            {
                int[] selected = _edges.ToArray();
                _edges.Clear();
                _vertices.Clear();
                foreach (int edgeId in selected)
                {
                    if (network.SplitEdge(edgeId) is int vertex)
                    {
                        _vertices.Add(vertex);
                    }
                }
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.M, false) && _vertices.Count > 1)
        {
            Mutate(component, "Merge procedural mesh vertices", network =>
            {
                int? kept = network.MergeVertices(_vertices);
                _vertices.Clear();
                if (kept is int id)
                {
                    _vertices.Add(id);
                }
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.E, false) && (_vertices.Count > 0 || _edges.Count > 0))
        {
            ProceduralMeshNetwork before = component.Network.Clone();
            PreviewMutate(component, network =>
            {
                IReadOnlyList<int> extruded = network.Extrude(_vertices, _edges, GVector3.Zero);
                _vertices.Clear();
                _edges.Clear();
                foreach (int id in extruded)
                {
                    _vertices.Add(id);
                }
            });
            BeginModal(component, active.Entity, ProceduralMeshModalMode.Translate, before, "Extrude procedural mesh selection");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.D, false) && Godot.Input.IsPhysicalKeyPressed(Key.Shift) && (_vertices.Count > 0 || _edges.Count > 0))
        {
            ProceduralMeshNetwork before = component.Network.Clone();
            PreviewMutate(component, network =>
            {
                IReadOnlyList<int> duplicated = network.DuplicateSubgraph(_vertices, _edges, GVector3.Zero);
                _vertices.Clear();
                _edges.Clear();
                foreach (int id in duplicated)
                {
                    _vertices.Add(id);
                }
            });
            BeginModal(component, active.Entity, ProceduralMeshModalMode.Translate, before, "Duplicate procedural mesh selection");
        }
    }

    private void BeginModal(
        ProceduralMeshComponent component,
        SceneEntity entity,
        ProceduralMeshModalMode mode,
        ProceduralMeshNetwork before,
        string description)
    {
        _modalMode = mode;
        _modalAxis = -1;
        _modalStartMouse = ImGui.GetMousePos();
        _modalStartPivot = ComputePivot(component, entity);
        _modalBefore = before.Clone();
        _modalDescription = description;
        _modalStartPositions.Clear();
        foreach (int id in _vertices)
        {
            if (component.Network.Vertex(id) is { } vertex)
            {
                _modalStartPositions[id] = vertex.Position;
            }
        }
    }

    private void UpdateModal(ProceduralMeshComponent component, SceneEntity entity, in ViewportContext viewport)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.X, false)) { _modalAxis = _modalAxis == 0 ? -1 : 0; }
        if (ImGui.IsKeyPressed(ImGuiKey.Y, false)) { _modalAxis = _modalAxis == 1 ? -1 : 1; }
        if (ImGui.IsKeyPressed(ImGuiKey.Z, false)) { _modalAxis = _modalAxis == 2 ? -1 : 2; }

        Transform3D delta = _modalMode == ProceduralMeshModalMode.Translate
            ? ComputeModalTranslate(viewport.Camera, viewport.ImageMin)
            : ComputeModalRotate(viewport.Camera, viewport.ImageMin);

        ProceduralMeshNetwork changed = component.Network.Clone();
        foreach ((int id, GVector3 local) in _modalStartPositions)
        {
            changed.MoveVertex(id, entity.Transform.AffineInverse() * (delta * (entity.Transform * local)));
        }

        component.ReplaceNetwork(changed);
        _context.Scene.Touch(entity);
        DrawModalHud(viewport.ImageMin);

        bool confirm = ImGui.IsKeyPressed(ImGuiKey.Enter, false) ||
                       ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false) ||
                       ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool cancel = ImGui.IsKeyPressed(ImGuiKey.Escape, false) ||
                      ImGui.IsMouseClicked(ImGuiMouseButton.Right);

        if (!confirm && !cancel)
        {
            return;
        }

        ProceduralMeshNetwork after = component.Network.Clone();
        ProceduralMeshNetwork before = _modalBefore ?? after;
        if (cancel)
        {
            component.ReplaceNetwork(before);
        }
        else if (before.Fingerprint() != after.Fingerprint())
        {
            _context.Sessions.Record(new SetProceduralMeshNetworkCommand(component, before, after, _modalDescription));
        }

        _modalMode = ProceduralMeshModalMode.None;
        _modalBefore = null;
        _modalStartPositions.Clear();
    }

    private Transform3D ComputeModalTranslate(Camera3D camera, NVector2 imageMin)
    {
        GVector3 pivot = _modalStartPivot.Origin;
        GVector3 move;
        if (_modalAxis < 0)
        {
            GVector3 normal = -camera.GlobalTransform.Basis.Z;
            move = RayToPlane(camera, ImGui.GetMousePos(), imageMin, pivot, normal) -
                   RayToPlane(camera, _modalStartMouse, imageMin, pivot, normal);
        }
        else
        {
            GVector3 axis = _context.Axes.UserAxis(_modalAxis);
            GVector3 normal = AxisDragPlaneNormal(axis, pivot, camera);
            float now = (RayToPlane(camera, ImGui.GetMousePos(), imageMin, pivot, normal) - pivot).Dot(axis);
            float start = (RayToPlane(camera, _modalStartMouse, imageMin, pivot, normal) - pivot).Dot(axis);
            move = axis * (now - start);
        }

        return new Transform3D(Basis.Identity, move);
    }

    private Transform3D ComputeModalRotate(Camera3D camera, NVector2 imageMin)
    {
        GVector3 pivot = _modalStartPivot.Origin;
        GVector3 axis = _modalAxis < 0 ? (camera.GlobalPosition - pivot).Normalized() : _context.Axes.UserAxis(_modalAxis);
        ObjectSelection.WorldToScreen(camera, pivot, imageMin, out NVector2 centre);
        NVector2 mouse = ImGui.GetMousePos();
        float now = Mathf.Atan2(mouse.Y - centre.Y, mouse.X - centre.X);
        float start = Mathf.Atan2(_modalStartMouse.Y - centre.Y, _modalStartMouse.X - centre.X);
        float facing = axis.Dot(camera.GlobalPosition - pivot);
        float angle = -(now - start) * (facing >= 0.0f ? 1.0f : -1.0f);
        Basis rotation = new(axis, angle);
        return new Transform3D(rotation, pivot - rotation * pivot);
    }

    private void DrawModalHud(NVector2 imageMin)
    {
        string op = _modalMode == ProceduralMeshModalMode.Translate ? "Move" : "Rotate";
        string axis = _modalAxis < 0 ? "" : $" {"XYZ".Substring(_modalAxis, 1)}";
        ImGui.GetWindowDrawList().AddText(
            imageMin + new NVector2(8.0f, 6.0f),
            ImGui.GetColorU32(new NVector4(1.0f, 0.85f, 0.35f, 1.0f)),
            $"{op}{axis}   (LMB/Enter confirm, RMB/Esc cancel, X/Y/Z axis)");
    }

    private void DriveGizmo(ProceduralMeshComponent component, SceneEntity entity, in ViewportContext viewport)
    {
        if (_vertices.Count == 0)
        {
            return;
        }

        Transform3D pivot = ComputePivot(component, entity);
        if (_dragging)
        {
            pivot = _dragStartPivot;
        }

        bool wasUsing = _gizmo.IsUsing;
        _gizmo.Manipulate(viewport.Camera, viewport.ImageMin, viewport.ImageSize, viewport.Hovered, ref pivot);

        if (_gizmo.IsUsing && !wasUsing)
        {
            _dragging = true;
            _dragStartPivot = ComputePivot(component, entity);
            _dragStartPositions.Clear();
            foreach (int id in _vertices)
            {
                if (component.Network.Vertex(id) is { } vertex)
                {
                    _dragStartPositions[id] = vertex.Position;
                }
            }
        }

        if (_gizmo.IsUsing)
        {
            Transform3D delta = pivot * _dragStartPivot.AffineInverse();
            ProceduralMeshNetwork changed = component.Network.Clone();
            foreach ((int id, GVector3 local) in _dragStartPositions)
            {
                changed.MoveVertex(id, entity.Transform.AffineInverse() * (delta * (entity.Transform * local)));
            }

            component.ReplaceNetwork(changed);
            _context.Scene.Touch(entity);
        }

        if (wasUsing && !_gizmo.IsUsing && _dragging)
        {
            ProceduralMeshNetwork before = component.Network.Clone();
            foreach ((int id, GVector3 local) in _dragStartPositions)
            {
                before.MoveVertex(id, local);
            }

            ProceduralMeshNetwork after = component.Network.Clone();
            if (before.Fingerprint() != after.Fingerprint())
            {
                _context.Sessions.Record(new SetProceduralMeshNetworkCommand(component, before, after, "Transform procedural mesh vertices"));
            }

            _dragging = false;
        }
    }

    private Transform3D ComputePivot(ProceduralMeshComponent component, SceneEntity entity)
    {
        GVector3 origin = GVector3.Zero;
        int count = 0;
        foreach (int id in _vertices)
        {
            if (component.Network.Vertex(id) is { } vertex)
            {
                origin += entity.Transform * vertex.Position;
                count++;
            }
        }

        return new Transform3D(Basis.Identity, count == 0 ? entity.Transform.Origin : origin / count);
    }

    private void HandlePointer(ProceduralMeshComponent component, SceneEntity entity, bool canStartClick, in ViewportContext viewport)
    {
        NVector2 mouse = ImGui.GetMousePos();

        if (!_mouseDown && canStartClick && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _mouseDown = true;
            _marquee = false;
            _marqueeStart = mouse;
        }

        if (!_mouseDown)
        {
            return;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_marquee && (mouse - _marqueeStart).Length() > MarqueeThreshold)
            {
                _marquee = true;
            }

            if (_marquee)
            {
                DrawMarquee(_marqueeStart, mouse);
            }

            return;
        }

        _mouseDown = false;
        if (_marquee)
        {
            ApplyBoxSelection(component, entity, _marqueeStart, mouse, viewport.Camera, viewport.ImageMin);
        }
        else
        {
            HandleClick(component, entity, viewport);
        }

        _marquee = false;
    }

    private void HandleClick(ProceduralMeshComponent component, SceneEntity entity, in ViewportContext viewport)
    {
        bool additive = Godot.Input.IsPhysicalKeyPressed(Key.Shift);
        bool ctrl = Godot.Input.IsPhysicalKeyPressed(Key.Ctrl);

        if (ctrl && TryMouseOnLocalPlane(entity, viewport, out GVector3 local))
        {
            Mutate(component, "Add procedural mesh vertex", network =>
            {
                int id = network.AddVertex(local);
                _vertices.Clear();
                _edges.Clear();
                _vertices.Add(id);
            });
            return;
        }

        if (_mode == ProceduralMeshSelectionMode.Vertex &&
            TryPickVertex(component, entity, viewport.Camera, viewport.ImageMin, out int vertexId))
        {
            ToggleOnly(_vertices, vertexId, additive);
            if (!additive)
            {
                _edges.Clear();
            }

            return;
        }

        if (_mode == ProceduralMeshSelectionMode.Edge &&
            TryPickEdge(component, entity, viewport.Camera, viewport.ImageMin, out int edgeId))
        {
            ToggleOnly(_edges, edgeId, additive);
            if (!additive)
            {
                _vertices.Clear();
            }

            return;
        }

        if (!additive)
        {
            _vertices.Clear();
            _edges.Clear();
        }
    }

    private void ApplyBoxSelection(
        ProceduralMeshComponent component,
        SceneEntity entity,
        NVector2 a,
        NVector2 b,
        Camera3D camera,
        NVector2 imageMin)
    {
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        bool additive = Godot.Input.IsPhysicalKeyPressed(Key.Shift);

        if (!additive)
        {
            _vertices.Clear();
            _edges.Clear();
        }

        if (_mode == ProceduralMeshSelectionMode.Vertex)
        {
            foreach (ProceduralMeshVertex vertex in component.Network.Vertices)
            {
                if (Project(camera, imageMin, entity.Transform * vertex.Position, out NVector2 screen) && Inside(screen, min, max))
                {
                    _vertices.Add(vertex.Id);
                }
            }

            return;
        }

        foreach (ProceduralMeshEdge edge in component.Network.Edges)
        {
            if (component.Network.Vertex(edge.A) is not { } va || component.Network.Vertex(edge.B) is not { } vb)
            {
                continue;
            }

            GVector3 midpoint = (va.Position + vb.Position) * 0.5f;
            if (Project(camera, imageMin, entity.Transform * midpoint, out NVector2 screen) && Inside(screen, min, max))
            {
                _edges.Add(edge.Id);
            }
        }
    }

    private static bool Inside(NVector2 point, NVector2 min, NVector2 max) =>
        point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

    private static void ToggleOnly(HashSet<int> set, int id, bool additive)
    {
        if (!additive)
        {
            set.Clear();
            set.Add(id);
            return;
        }

        if (!set.Remove(id))
        {
            set.Add(id);
        }
    }

    private static void DrawMarquee(NVector2 a, NVector2 b)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new NVector4(0.30f, 0.55f, 0.95f, 0.20f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new NVector4(0.40f, 0.65f, 1.0f, 0.90f)));
    }

    private void DrawNetwork((SceneEntity Entity, ProceduralMeshComponent Component) active, Camera3D camera, NVector2 imageMin)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint edgeColor = ImGui.GetColorU32(new NVector4(0.35f, 0.78f, 1.0f, 0.85f));
        uint selectedColor = ImGui.GetColorU32(new NVector4(1.0f, 0.72f, 0.2f, 1.0f));
        uint vertexColor = ImGui.GetColorU32(new NVector4(0.92f, 0.94f, 0.96f, 1.0f));

        foreach (ProceduralMeshEdge edge in active.Component.Network.Edges)
        {
            if (active.Component.Network.Vertex(edge.A) is not { } a || active.Component.Network.Vertex(edge.B) is not { } b)
            {
                continue;
            }

            if (Project(camera, imageMin, active.Entity.Transform * a.Position, out NVector2 sa) &&
                Project(camera, imageMin, active.Entity.Transform * b.Position, out NVector2 sb))
            {
                drawList.AddLine(sa, sb, _edges.Contains(edge.Id) ? selectedColor : edgeColor, _edges.Contains(edge.Id) ? 3.0f : 2.0f);
            }
        }

        foreach (ProceduralMeshVertex vertex in active.Component.Network.Vertices)
        {
            if (Project(camera, imageMin, active.Entity.Transform * vertex.Position, out NVector2 screen))
            {
                bool selected = _vertices.Contains(vertex.Id);
                drawList.AddCircleFilled(screen, selected ? 6.0f : 4.5f, selected ? selectedColor : vertexColor, 16);
                drawList.AddCircle(screen, selected ? 6.0f : 4.5f, ImGui.GetColorU32(new NVector4(0.05f, 0.06f, 0.07f, 0.95f)), 16, 1.3f);
            }
        }
    }

    private static bool TryPickVertex(ProceduralMeshComponent component, SceneEntity entity, Camera3D camera, NVector2 imageMin, out int id)
    {
        id = 0;
        NVector2 mouse = ImGui.GetMousePos();
        float best = VertexPickRadius;
        foreach (ProceduralMeshVertex vertex in component.Network.Vertices)
        {
            if (!Project(camera, imageMin, entity.Transform * vertex.Position, out NVector2 screen))
            {
                continue;
            }

            float distance = (screen - mouse).Length();
            if (distance < best)
            {
                best = distance;
                id = vertex.Id;
            }
        }

        return id != 0;
    }

    private static bool TryPickEdge(ProceduralMeshComponent component, SceneEntity entity, Camera3D camera, NVector2 imageMin, out int id)
    {
        id = 0;
        NVector2 mouse = ImGui.GetMousePos();
        float best = EdgePickRadius;
        foreach (ProceduralMeshEdge edge in component.Network.Edges)
        {
            if (component.Network.Vertex(edge.A) is not { } a || component.Network.Vertex(edge.B) is not { } b)
            {
                continue;
            }

            if (!Project(camera, imageMin, entity.Transform * a.Position, out NVector2 sa) ||
                !Project(camera, imageMin, entity.Transform * b.Position, out NVector2 sb))
            {
                continue;
            }

            float distance = DistanceToSegment(mouse, sa, sb);
            if (distance < best)
            {
                best = distance;
                id = edge.Id;
            }
        }

        return id != 0;
    }

    private bool TryMouseOnLocalPlane(SceneEntity entity, in ViewportContext viewport, out GVector3 local)
    {
        NVector2 mouse = ImGui.GetMousePos();
        GVector2 inViewport = new(mouse.X - viewport.ImageMin.X, mouse.Y - viewport.ImageMin.Y);
        GVector3 origin = viewport.Camera.ProjectRayOrigin(inViewport);
        GVector3 dir = viewport.Camera.ProjectRayNormal(inViewport);
        GVector3 planeNormal = entity.Transform.Basis.Y.Normalized();
        float denom = dir.Dot(planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            local = default;
            return false;
        }

        GVector3 world = origin + dir * ((entity.Transform.Origin - origin).Dot(planeNormal) / denom);
        local = entity.Transform.AffineInverse() * world;
        return true;
    }

    private void Mutate(ProceduralMeshComponent component, string description, Action<ProceduralMeshNetwork> mutate)
    {
        ProceduralMeshNetwork before = component.Network.Clone();
        ProceduralMeshNetwork after = component.Network.Clone();
        mutate(after);
        if (before.Fingerprint() == after.Fingerprint())
        {
            return;
        }

        var command = new SetProceduralMeshNetworkCommand(component, before, after, description);
        command.Apply();
        _context.Sessions.Record(command);
        if (component.Owner is { } owner)
        {
            _context.Scene.Touch(owner);
        }
    }

    private void PreviewMutate(ProceduralMeshComponent component, Action<ProceduralMeshNetwork> mutate)
    {
        ProceduralMeshNetwork after = component.Network.Clone();
        mutate(after);
        component.ReplaceNetwork(after);
        if (component.Owner is { } owner)
        {
            _context.Scene.Touch(owner);
        }
    }

    private static GVector3 RayToPlane(Camera3D camera, NVector2 mouse, NVector2 imageMin, GVector3 planePoint, GVector3 planeNormal)
    {
        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        GVector3 origin = camera.ProjectRayOrigin(local);
        GVector3 dir = camera.ProjectRayNormal(local);
        float denom = dir.Dot(planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            return planePoint;
        }

        return origin + dir * ((planePoint - origin).Dot(planeNormal) / denom);
    }

    private static GVector3 AxisDragPlaneNormal(GVector3 axisDir, GVector3 origin, Camera3D camera)
    {
        GVector3 viewDir = (origin - camera.GlobalPosition).Normalized();
        GVector3 normal = viewDir - axisDir * viewDir.Dot(axisDir);
        if (normal.LengthSquared() < 1e-8f)
        {
            normal = axisDir.Cross(camera.GlobalTransform.Basis.Y);
            if (normal.LengthSquared() < 1e-8f)
            {
                normal = axisDir.Cross(camera.GlobalTransform.Basis.X);
            }
        }

        return normal.Normalized();
    }

    private static bool Project(Camera3D camera, NVector2 imageMin, GVector3 world, out NVector2 screen)
    {
        if (camera.IsPositionBehind(world))
        {
            screen = default;
            return false;
        }

        GVector2 p = camera.UnprojectPosition(world);
        screen = new NVector2(imageMin.X + p.X, imageMin.Y + p.Y);
        return true;
    }

    private static float DistanceToSegment(NVector2 p, NVector2 a, NVector2 b)
    {
        NVector2 ab = b - a;
        float lenSq = ab.X * ab.X + ab.Y * ab.Y;
        if (lenSq < 1e-6f)
        {
            return (p - a).Length();
        }

        float t = Math.Clamp(Vector2Dot(p - a, ab) / lenSq, 0.0f, 1.0f);
        NVector2 projection = a + ab * t;
        return (p - projection).Length();
    }

    private static float Vector2Dot(NVector2 a, NVector2 b) => a.X * b.X + a.Y * b.Y;
}
