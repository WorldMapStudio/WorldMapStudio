using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

public enum NetworkSelectionMode
{
    Vertex,
    Edge,
    Face,
}

public enum NetworkModalMode
{
    None,
    Translate,
    Rotate,
    Scale,
}

/// <summary>
/// Edits any <see cref="INetworkEditable"/>'s vertex/edge graph in the viewport: select, marquee,
/// G/R/S modal transforms, extrude, duplicate, split, merge. One tool serves every procedural mesh —
/// which bound function it is (an ordinary mesh, a paint-only road) is entirely decided by
/// <see cref="INetworkEditable.Paint"/> and <see cref="INetworkEditable.PlanarXZ"/>, so a mesh function
/// and a paint function share every line of editing behaviour here and differ only in what they do
/// with the resulting graph.
///
/// <see cref="INetworkEditable.PlanarXZ"/> functions (a road) get terrain-aware placement and display:
/// a new vertex is dropped onto the terrain under the cursor rather than the entity's local plane, and
/// every drawn vertex is projected onto the terrain for display even though its stored position is
/// flat. Rotation locks to the vertical axis, and every transform's result has its height zeroed —
/// the data is authored purely horizontal, so nothing is ever allowed to introduce a height component.
/// </summary>
public sealed class NetworkEditTool : ITool
{
    private const float MarqueeThreshold = 5.0f;
    private const float VertexPickRadius = 9.0f;
    private const float EdgePickRadius = 6.0f;

    private readonly ToolContext _context;
    private readonly TerrainProbe _terrain;
    private readonly Func<SceneEntity, INetworkEditable?> _selector;
    private readonly string _name;
    private readonly TransformGizmo _gizmo = new();
    private readonly HashSet<int> _vertices = [];
    private readonly HashSet<int> _edges = [];
    private readonly HashSet<int> _faces = [];
    private NetworkSelectionMode _mode;

    private bool _dragging;
    private Transform3D _dragStartPivot;
    private readonly Dictionary<int, GVector3> _dragStartPositions = [];
    private bool _mouseDown;
    private bool _marquee;
    private NVector2 _marqueeStart;
    private NetworkModalMode _modalMode;
    private int _modalAxis = -1;
    private bool _modalAxisExclude;
    private NVector2 _modalStartMouse;
    private Transform3D _modalStartPivot;
    private VertexNetwork? _modalBefore;
    private readonly Dictionary<int, GVector3> _modalStartPositions = [];
    private string _modalNumeric = string.Empty;
    private string _modalDescription = "";

    public NetworkEditTool(ToolContext context, string name, Func<SceneEntity, INetworkEditable?> selector)
    {
        _context = context;
        _name = name;
        _selector = selector;
        _terrain = new TerrainProbe(context.Scene);
        _gizmo.Axes = context.Axes;
        _gizmo.LocalSpace = false;
    }

    public string Name => _name;

    public bool CapturesMouse => _gizmo.IsUsing || _mouseDown;

    public void DrawToolbar()
    {
        if (ImGui.RadioButton("Vertex", _mode == NetworkSelectionMode.Vertex))
        {
            _mode = NetworkSelectionMode.Vertex;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Edge", _mode == NetworkSelectionMode.Edge))
        {
            _mode = NetworkSelectionMode.Edge;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Face", _mode == NetworkSelectionMode.Face))
        {
            _mode = NetworkSelectionMode.Face;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move", _gizmo.Operation == GizmoOperation.Translate))
        {
            _gizmo.Operation = GizmoOperation.Translate;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate", _gizmo.Operation == GizmoOperation.Rotate))
        {
            _gizmo.Operation = GizmoOperation.Rotate;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Scale", _gizmo.Operation == GizmoOperation.Scale))
        {
            _gizmo.Operation = GizmoOperation.Scale;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled($"{_vertices.Count} vertices, {_edges.Count} edges, {_faces.Count} faces");
    }

    public void UpdateViewport(in ViewportContext viewport)
    {
        if (Active() is not { } component || viewport.CameraFlying)
        {
            _vertices.Clear();
            _edges.Clear();
            _faces.Clear();
            return;
        }

        SceneEntity entity = component.Owner!;
        DrawNetwork(component, entity, viewport.Camera, viewport.ImageMin);
        if (_modalMode != NetworkModalMode.None)
        {
            UpdateModal(component, entity, viewport);
            return;
        }

        HandleKeys(component, entity);
        DriveGizmo(component, entity, viewport);
        HandlePointer(component, entity, viewport.Hovered && !_gizmo.IsUsing && !_gizmo.IsHovered, viewport);
    }

    private INetworkEditable? Active()
    {
        SceneEntity? entity = _context.Selection.Selected.OfType<SceneEntity>()
            .FirstOrDefault(entity => _context.Scene.Contains(entity) && _selector(entity) != null);
        return entity == null ? null : _selector(entity);
    }

    private void HandleKeys(INetworkEditable component, SceneEntity entity)
    {
        bool hasEffectiveSelection = EffectiveVertices(component).Any();

        if (ImGui.IsKeyPressed(ImGuiKey.G, false) && hasEffectiveSelection)
        {
            BeginModal(component, entity, NetworkModalMode.Translate, component.Network.Clone(), "Move network vertices");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.R, false) && hasEffectiveSelection)
        {
            BeginModal(component, entity, NetworkModalMode.Rotate, component.Network.Clone(), "Rotate network vertices");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.S, false) && hasEffectiveSelection && !Godot.Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            BeginModal(component, entity, NetworkModalMode.Scale, component.Network.Clone(), "Scale network vertices");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) || ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
        {
            Mutate(component, "Delete network selection", network =>
            {
                foreach (int faceId in _faces.ToArray())
                {
                    network.RemoveFace(faceId);
                }

                foreach (int edgeId in _edges.ToArray())
                {
                    network.RemoveEdge(edgeId);
                }

                foreach (int vertexId in _vertices.ToArray())
                {
                    network.RemoveVertex(vertexId);
                }

                _faces.Clear();
                _edges.Clear();
                _vertices.Clear();
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.F, false) && _vertices.Count == 2)
        {
            Mutate(component, "Connect network vertices", network =>
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
        else if (ImGui.IsKeyPressed(ImGuiKey.F, false) && _edges.Count >= 3 &&
                 TryComputeEdgeLoop(component.Network, _edges) is { } loop)
        {
            FillFace(component, loop);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.F, false) && _vertices.Count >= 3)
        {
            // No selected edges describe a boundary (or they don't form one simple loop) — connect
            // the selected vertices in id/creation order, the same fallback Blender's own "Make
            // Edge/Face" uses for a loose vertex selection with no existing connectivity to respect.
            FillFace(component, _vertices.OrderBy(id => id).ToArray());
        }

        if (ImGui.IsKeyPressed(ImGuiKey.S, false) && _edges.Count > 0 && Godot.Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            Mutate(component, "Split network edges", network =>
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
            Mutate(component, "Merge network vertices", network =>
            {
                int? kept = network.MergeVertices(_vertices);
                _vertices.Clear();
                if (kept is int id)
                {
                    _vertices.Add(id);
                }
            });
        }

        if (ImGui.IsKeyPressed(ImGuiKey.E, false) && (_vertices.Count > 0 || _edges.Count > 0 || _faces.Count > 0))
        {
            VertexNetwork before = component.Network.Clone();
            PreviewMutate(component, network =>
            {
                NetworkSubgraph extruded = network.Extrude(_vertices, _edges, _faces, GVector3.Zero);
                _vertices.Clear();
                _edges.Clear();
                _faces.Clear();
                foreach (int id in extruded.VertexIds)
                {
                    _vertices.Add(id);
                }

                foreach (int id in extruded.FaceIds)
                {
                    _faces.Add(id);
                }
            });
            BeginModal(component, entity, NetworkModalMode.Translate, before, "Extrude network selection");
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.D, false) && Godot.Input.IsPhysicalKeyPressed(Key.Shift) && (_vertices.Count > 0 || _edges.Count > 0 || _faces.Count > 0))
        {
            VertexNetwork before = component.Network.Clone();
            PreviewMutate(component, network =>
            {
                NetworkSubgraph duplicated = network.DuplicateSubgraph(_vertices, _edges, _faces, GVector3.Zero);
                _vertices.Clear();
                _edges.Clear();
                _faces.Clear();
                foreach (int id in duplicated.VertexIds)
                {
                    _vertices.Add(id);
                }

                foreach (int id in duplicated.FaceIds)
                {
                    _faces.Add(id);
                }
            });
            BeginModal(component, entity, NetworkModalMode.Translate, before, "Duplicate network selection");
        }
    }

    /// <summary>Fills a face from an ordered vertex loop and selects it — the shared tail of both F
    /// paths (an edge-loop boundary, or a loose vertex selection with no boundary to respect).</summary>
    private void FillFace(INetworkEditable component, IReadOnlyList<int> loop)
    {
        Mutate(component, "Fill network face", network =>
        {
            int? face = network.AddFace(loop);
            _vertices.Clear();
            _edges.Clear();
            _faces.Clear();
            if (face is int id)
            {
                _faces.Add(id);
                _mode = NetworkSelectionMode.Face;
            }
        });
    }

    private void BeginModal(
        INetworkEditable component,
        SceneEntity entity,
        NetworkModalMode mode,
        VertexNetwork before,
        string description)
    {
        _modalMode = mode;
        _modalAxis = -1;
        _modalAxisExclude = false;
        _modalStartMouse = ImGui.GetMousePos();
        _modalStartPivot = ComputePivot(component, entity);
        _modalBefore = before.Clone();
        _modalNumeric = string.Empty;
        _modalDescription = description;
        _modalStartPositions.Clear();
        foreach (int id in EffectiveVertices(component))
        {
            if (component.Network.Vertex(id) is { } vertex)
            {
                _modalStartPositions[id] = vertex.Position;
            }
        }
    }

    private void UpdateModal(INetworkEditable component, SceneEntity entity, in ViewportContext viewport)
    {
        HandleModalAxisKey(ImGuiKey.X, 0);
        HandleModalAxisKey(ImGuiKey.Y, 1);
        HandleModalAxisKey(ImGuiKey.Z, 2);
        HandleModalNumericKeys();

        Transform3D delta = _modalMode switch
        {
            NetworkModalMode.Translate => ComputeModalTranslate(viewport.Camera, viewport.ImageMin),
            NetworkModalMode.Rotate => ComputeModalRotate(component, viewport.Camera, viewport.ImageMin),
            NetworkModalMode.Scale => ComputeModalScale(viewport.Camera, viewport.ImageMin),
            _ => Transform3D.Identity,
        };

        VertexNetwork changed = component.Network.Clone();
        foreach ((int id, GVector3 local) in _modalStartPositions)
        {
            changed.MoveVertex(id, ApplyDelta(component, entity.Transform, delta, local));
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

        VertexNetwork after = component.Network.Clone();
        VertexNetwork before = _modalBefore ?? after;
        if (cancel)
        {
            component.ReplaceNetwork(before);
        }
        else if (before.Fingerprint() != after.Fingerprint())
        {
            _context.Sessions.Record(new SetNetworkCommand(component, before, after, _modalDescription));
        }

        _modalMode = NetworkModalMode.None;
        _modalAxisExclude = false;
        _modalBefore = null;
        _modalStartPositions.Clear();
        _modalNumeric = string.Empty;
    }

    private void HandleModalAxisKey(ImGuiKey key, int axis)
    {
        if (!ImGui.IsKeyPressed(key, false))
        {
            return;
        }

        bool exclude = _modalMode != NetworkModalMode.Rotate && Godot.Input.IsPhysicalKeyPressed(Key.Shift);
        if (_modalAxis != axis || _modalAxisExclude != exclude)
        {
            _modalAxis = axis;
            _modalAxisExclude = exclude;
            return;
        }

        _modalAxis = -1;
        _modalAxisExclude = false;
    }

    private Transform3D ComputeModalTranslate(Camera3D camera, NVector2 imageMin)
    {
        GVector3 pivot = _modalStartPivot.Origin;
        GVector3 move;
        if (TryModalNumeric(out float number))
        {
            if (_modalAxis >= 0 && _modalAxisExclude)
            {
                GVector3 planeMove = PlaneTranslate(camera, ImGui.GetMousePos(), imageMin, pivot, _context.Axes.UserAxis(_modalAxis));
                move = planeMove.LengthSquared() < 1e-8f ? GVector3.Zero : planeMove.Normalized() * number;
            }
            else
            {
                move = _context.Axes.UserAxis(_modalAxis < 0 ? 0 : _modalAxis) * number;
            }
        }
        else if (_modalAxis < 0)
        {
            GVector3 normal = -camera.GlobalTransform.Basis.Z;
            move = RayToPlane(camera, ImGui.GetMousePos(), imageMin, pivot, normal) -
                   RayToPlane(camera, _modalStartMouse, imageMin, pivot, normal);
        }
        else if (_modalAxisExclude)
        {
            move = PlaneTranslate(camera, ImGui.GetMousePos(), imageMin, pivot, _context.Axes.UserAxis(_modalAxis));
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

    private Transform3D ComputeModalRotate(INetworkEditable component, Camera3D camera, NVector2 imageMin)
    {
        GVector3 pivot = _modalStartPivot.Origin;
        GVector3 axis = component.PlanarXZ
            ? GVector3.Up
            : (_modalAxis < 0 ? (camera.GlobalPosition - pivot).Normalized() : _context.Axes.UserAxis(_modalAxis));
        float angle;
        if (TryModalNumeric(out float number))
        {
            angle = Mathf.DegToRad(number);
        }
        else
        {
            ObjectSelection.WorldToScreen(camera, pivot, imageMin, out NVector2 centre);
            NVector2 mouse = ImGui.GetMousePos();
            float now = Mathf.Atan2(mouse.Y - centre.Y, mouse.X - centre.X);
            float start = Mathf.Atan2(_modalStartMouse.Y - centre.Y, _modalStartMouse.X - centre.X);
            float facing = axis.Dot(camera.GlobalPosition - pivot);
            angle = -(now - start) * (facing >= 0.0f ? 1.0f : -1.0f);
        }

        Basis rotation = new(axis, angle);
        return new Transform3D(rotation, pivot - rotation * pivot);
    }

    private Transform3D ComputeModalScale(Camera3D camera, NVector2 imageMin)
    {
        GVector3 pivot = _modalStartPivot.Origin;
        float factor = TryModalNumeric(out float number)
            ? number
            : ScaleFactorFromScreen(camera, imageMin, pivot, _modalStartMouse, ImGui.GetMousePos());
        factor = Mathf.Max(0.01f, factor);
        if (_modalAxis < 0)
        {
            return ScaleAround(pivot, factor);
        }

        GVector3 axis = _context.Axes.UserAxis(_modalAxis);
        return _modalAxisExclude
            ? ScaleAroundExcludingAxis(pivot, axis, factor)
            : ScaleAround(pivot, axis, factor);
    }

    private void DrawModalHud(NVector2 imageMin)
    {
        string op = _modalMode switch
        {
            NetworkModalMode.Translate => "Move",
            NetworkModalMode.Rotate => "Rotate",
            NetworkModalMode.Scale => "Scale",
            _ => "Transform",
        };
        string axis = string.Empty;
        if (_modalAxis >= 0)
        {
            string axisLabel = _modalAxisExclude ? "XYZ".Remove(_modalAxis, 1) : "XYZ".Substring(_modalAxis, 1);
            axis = $" {axisLabel}";
        }
        string value = _modalNumeric.Length > 0
            ? $": {_modalNumeric}{(_modalMode == NetworkModalMode.Rotate ? " deg" : string.Empty)}"
            : string.Empty;
        ImGui.GetWindowDrawList().AddText(
            imageMin + new NVector2(8.0f, 6.0f),
            ImGui.GetColorU32(new NVector4(1.0f, 0.85f, 0.35f, 1.0f)),
            $"{op}{axis}{value}   (LMB/Enter confirm, RMB/Esc cancel, X/Y/Z axis, type a value)");
    }

    private void HandleModalNumericKeys()
    {
        for (int d = 0; d <= 9; d++)
        {
            if (ImGui.IsKeyPressed(ImGuiKey._0 + d, false) || ImGui.IsKeyPressed(ImGuiKey.Keypad0 + d, false))
            {
                _modalNumeric += (char)('0' + d);
            }
        }

        if ((ImGui.IsKeyPressed(ImGuiKey.Period, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadDecimal, false)) &&
            !_modalNumeric.Contains('.'))
        {
            _modalNumeric += _modalNumeric.Length == 0 ? "0." : ".";
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Minus, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadSubtract, false))
        {
            _modalNumeric = _modalNumeric.StartsWith('-') ? _modalNumeric[1..] : "-" + _modalNumeric;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false) && _modalNumeric.Length > 0)
        {
            _modalNumeric = _modalNumeric[..^1];
        }
    }

    private bool TryModalNumeric(out float value)
    {
        if (_modalNumeric.Length == 0 || _modalNumeric == "-")
        {
            value = 0.0f;
            return _modalNumeric.Length > 0;
        }

        return float.TryParse(_modalNumeric, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || SetZero(out value);

        static bool SetZero(out float v)
        {
            v = 0.0f;
            return true;
        }
    }

    private void DriveGizmo(INetworkEditable component, SceneEntity entity, in ViewportContext viewport)
    {
        if (!EffectiveVertices(component).Any())
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
            foreach (int id in EffectiveVertices(component))
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
            VertexNetwork changed = component.Network.Clone();
            foreach ((int id, GVector3 local) in _dragStartPositions)
            {
                changed.MoveVertex(id, ApplyDelta(component, entity.Transform, delta, local));
            }

            component.ReplaceNetwork(changed);
            _context.Scene.Touch(entity);
        }

        if (wasUsing && !_gizmo.IsUsing && _dragging)
        {
            VertexNetwork before = component.Network.Clone();
            foreach ((int id, GVector3 local) in _dragStartPositions)
            {
                before.MoveVertex(id, local);
            }

            VertexNetwork after = component.Network.Clone();
            if (before.Fingerprint() != after.Fingerprint())
            {
                _context.Sessions.Record(new SetNetworkCommand(component, before, after, "Transform network vertices"));
            }

            _dragging = false;
        }
    }

    /// <summary>Applies a world-space delta to one vertex's local position, flattening the result for
    /// planar networks so no transform can introduce a height component.</summary>
    private static GVector3 ApplyDelta(INetworkEditable component, Transform3D entityTransform, Transform3D delta, GVector3 local)
    {
        GVector3 moved = entityTransform.AffineInverse() * (delta * (entityTransform * local));
        if (component.PlanarXZ)
        {
            moved.Y = 0.0f;
        }

        return moved;
    }

    private Transform3D ComputePivot(INetworkEditable component, SceneEntity entity)
    {
        GVector3 origin = GVector3.Zero;
        int count = 0;
        foreach (int id in EffectiveVertices(component))
        {
            if (component.Network.Vertex(id) is { } vertex)
            {
                origin += Display(component, entity, vertex.Position);
                count++;
            }
        }

        return new Transform3D(Basis.Identity, count == 0 ? entity.Transform.Origin : origin / count);
    }

    /// <summary>Every vertex a transform (G/R/S, the gizmo) should move: the raw vertex selection,
    /// plus the endpoints of selected edges, plus the loop of every selected face — Blender resolves
    /// a transform to vertices the same way regardless of which select mode picked them.</summary>
    private IEnumerable<int> EffectiveVertices(INetworkEditable component)
    {
        var result = new HashSet<int>(_vertices);
        foreach (int edgeId in _edges)
        {
            if (component.Network.Edge(edgeId) is { } edge)
            {
                result.Add(edge.A);
                result.Add(edge.B);
            }
        }

        foreach (int faceId in _faces)
        {
            if (component.Network.Face(faceId) is { } face)
            {
                foreach (int id in face.Vertices)
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    /// <summary>World position to draw or pick a vertex at. Planar networks project onto the terrain
    /// under the vertex even though the stored position is flat, so editing still reads as "on the
    /// ground" without the data carrying a height nothing else would agree with.</summary>
    private GVector3 Display(INetworkEditable component, SceneEntity entity, GVector3 local)
    {
        GVector3 world = entity.Transform * local;
        return component.PlanarXZ ? _terrain.DropToHeight(world) : world;
    }

    private void HandlePointer(INetworkEditable component, SceneEntity entity, bool canStartClick, in ViewportContext viewport)
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

    private void HandleClick(INetworkEditable component, SceneEntity entity, in ViewportContext viewport)
    {
        bool additive = Godot.Input.IsPhysicalKeyPressed(Key.Shift);
        bool ctrl = Godot.Input.IsPhysicalKeyPressed(Key.Ctrl);

        if (ctrl && TryPlacementPoint(component, entity, viewport, out GVector3 local))
        {
            Mutate(component, "Add network vertex", network =>
            {
                int id = network.AddVertex(local);
                _vertices.Clear();
                _edges.Clear();
                _faces.Clear();
                _vertices.Add(id);
            });
            return;
        }

        if (_mode == NetworkSelectionMode.Vertex &&
            TryPickVertex(component, entity, viewport.Camera, viewport.ImageMin, out int vertexId))
        {
            ToggleOnly(_vertices, vertexId, additive);
            if (!additive)
            {
                _edges.Clear();
                _faces.Clear();
            }

            return;
        }

        if (_mode == NetworkSelectionMode.Edge &&
            TryPickEdge(component, entity, viewport.Camera, viewport.ImageMin, out int edgeId))
        {
            ToggleOnly(_edges, edgeId, additive);
            if (!additive)
            {
                _vertices.Clear();
                _faces.Clear();
            }

            return;
        }

        if (_mode == NetworkSelectionMode.Face &&
            TryPickFace(component, entity, viewport.Camera, viewport.ImageMin, out int faceId))
        {
            ToggleOnly(_faces, faceId, additive);
            if (!additive)
            {
                _vertices.Clear();
                _edges.Clear();
            }

            return;
        }

        if (!additive)
        {
            _vertices.Clear();
            _edges.Clear();
            _faces.Clear();
        }
    }

    private void ApplyBoxSelection(
        INetworkEditable component,
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
            _faces.Clear();
        }

        if (_mode == NetworkSelectionMode.Vertex)
        {
            foreach (NetworkVertex vertex in component.Network.Vertices)
            {
                if (Project(camera, imageMin, Display(component, entity, vertex.Position), out NVector2 screen) && Inside(screen, min, max))
                {
                    _vertices.Add(vertex.Id);
                }
            }

            return;
        }

        if (_mode == NetworkSelectionMode.Face)
        {
            foreach (NetworkFace face in component.Network.Faces)
            {
                if (FaceCentroid(component, face) is not { } centroid)
                {
                    continue;
                }

                if (Project(camera, imageMin, Display(component, entity, centroid), out NVector2 screen) && Inside(screen, min, max))
                {
                    _faces.Add(face.Id);
                }
            }

            return;
        }

        foreach (NetworkEdge edge in component.Network.Edges)
        {
            if (component.Network.Vertex(edge.A) is not { } va || component.Network.Vertex(edge.B) is not { } vb)
            {
                continue;
            }

            GVector3 midpoint = (va.Position + vb.Position) * 0.5f;
            if (Project(camera, imageMin, Display(component, entity, midpoint), out NVector2 screen) && Inside(screen, min, max))
            {
                _edges.Add(edge.Id);
            }
        }
    }

    /// <summary>The average of a face's authored vertex positions, or null if any vertex is missing.</summary>
    private static GVector3? FaceCentroid(INetworkEditable component, NetworkFace face)
    {
        GVector3 sum = GVector3.Zero;
        foreach (int id in face.Vertices)
        {
            if (component.Network.Vertex(id) is not { } vertex)
            {
                return null;
            }

            sum += vertex.Position;
        }

        return face.Vertices.Count == 0 ? null : sum / face.Vertices.Count;
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

    private void DrawNetwork(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint edgeColor = ImGui.GetColorU32(new NVector4(0.35f, 0.78f, 1.0f, 0.85f));
        uint selectedColor = ImGui.GetColorU32(new NVector4(1.0f, 0.72f, 0.2f, 1.0f));
        uint vertexColor = ImGui.GetColorU32(new NVector4(0.92f, 0.94f, 0.96f, 1.0f));

        if (component.Paint.Strokes.Count > 0)
        {
            DrawPaintOverlay(component, entity, camera, imageMin);
        }
        else
        {
            uint faceColor = ImGui.GetColorU32(new NVector4(0.35f, 0.78f, 1.0f, 0.12f));
            uint selectedFaceColor = ImGui.GetColorU32(new NVector4(1.0f, 0.72f, 0.2f, 0.35f));
            foreach (NetworkFace face in component.Network.Faces)
            {
                if (TryProjectFace(component, entity, camera, imageMin, face, out NVector2[] screen))
                {
                    DrawFaceFan(drawList, screen, _faces.Contains(face.Id) ? selectedFaceColor : faceColor);
                }
            }

            foreach (NetworkEdge edge in component.Network.Edges)
            {
                if (component.Network.Vertex(edge.A) is not { } a || component.Network.Vertex(edge.B) is not { } b)
                {
                    continue;
                }

                if (Project(camera, imageMin, Display(component, entity, a.Position), out NVector2 sa) &&
                    Project(camera, imageMin, Display(component, entity, b.Position), out NVector2 sb))
                {
                    drawList.AddLine(sa, sb, _edges.Contains(edge.Id) ? selectedColor : edgeColor, _edges.Contains(edge.Id) ? 3.0f : 2.0f);
                }
            }
        }

        foreach (NetworkVertex vertex in component.Network.Vertices)
        {
            if (Project(camera, imageMin, Display(component, entity, vertex.Position), out NVector2 screen))
            {
                bool selected = _vertices.Contains(vertex.Id);
                drawList.AddCircleFilled(screen, selected ? 6.0f : 4.5f, selected ? selectedColor : vertexColor, 16);
                drawList.AddCircle(screen, selected ? 6.0f : 4.5f, ImGui.GetColorU32(new NVector4(0.05f, 0.06f, 0.07f, 0.95f)), 16, 1.3f);
            }
        }
    }

    /// <summary>Draws a network's published paint strokes — the flattened shape plus each stroke's
    /// radius as an offset outline — rather than the raw straight edges between control vertices, which
    /// would lie about where a spline-fed shape (e.g. a road) actually goes and how wide it is.</summary>
    private void DrawPaintOverlay(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint centreColor = ImGui.GetColorU32(new NVector4(1.0f, 0.85f, 0.35f, 0.95f));
        uint widthColor = ImGui.GetColorU32(new NVector4(1.0f, 0.85f, 0.35f, 0.35f));

        // Several strokes commonly share one (A, B) segment (a road's centre and shoulder strokes,
        // for instance) — the centreline is drawn once per segment regardless of how many strokes
        // touch it, and every distinct radius among them gets its own offset pair.
        var drawnCentrelines = new HashSet<(GVector3 A, GVector3 B)>();
        foreach (ProceduralStroke stroke in component.Paint.Strokes)
        {
            if (drawnCentrelines.Add((stroke.A, stroke.B)))
            {
                DrawOffsetSegment(component, entity, camera, imageMin, drawList, stroke.A, stroke.B, 0.0f, centreColor, 2.5f);
            }

            if (stroke.Radius > 0.0f)
            {
                DrawOffsetSegment(component, entity, camera, imageMin, drawList, stroke.A, stroke.B, stroke.Radius, widthColor, 1.0f);
                DrawOffsetSegment(component, entity, camera, imageMin, drawList, stroke.A, stroke.B, -stroke.Radius, widthColor, 1.0f);
            }
        }
    }

    private void DrawOffsetSegment(
        INetworkEditable component,
        SceneEntity entity,
        Camera3D camera,
        NVector2 imageMin,
        ImDrawListPtr drawList,
        GVector3 segA,
        GVector3 segB,
        float offset,
        uint color,
        float thickness)
    {
        GVector3 a = segA;
        GVector3 b = segB;
        if (offset != 0.0f)
        {
            GVector2 direction = new(b.X - a.X, b.Z - a.Z);
            if (direction.LengthSquared() > 1e-8f)
            {
                GVector2 normal = new GVector2(-direction.Y, direction.X).Normalized() * offset;
                a = new GVector3(a.X + normal.X, a.Y, a.Z + normal.Y);
                b = new GVector3(b.X + normal.X, b.Y, b.Z + normal.Y);
            }
        }

        if (Project(camera, imageMin, Display(component, entity, a), out NVector2 sa) &&
            Project(camera, imageMin, Display(component, entity, b), out NVector2 sb))
        {
            drawList.AddLine(sa, sb, color, thickness);
        }
    }

    private bool TryPickVertex(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin, out int id)
    {
        id = 0;
        NVector2 mouse = ImGui.GetMousePos();
        float best = VertexPickRadius;
        foreach (NetworkVertex vertex in component.Network.Vertices)
        {
            if (!Project(camera, imageMin, Display(component, entity, vertex.Position), out NVector2 screen))
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

    private bool TryPickEdge(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin, out int id)
    {
        id = 0;
        NVector2 mouse = ImGui.GetMousePos();
        float best = EdgePickRadius;
        foreach (NetworkEdge edge in component.Network.Edges)
        {
            if (component.Network.Vertex(edge.A) is not { } a || component.Network.Vertex(edge.B) is not { } b)
            {
                continue;
            }

            if (!Project(camera, imageMin, Display(component, entity, a.Position), out NVector2 sa) ||
                !Project(camera, imageMin, Display(component, entity, b.Position), out NVector2 sb))
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

    /// <summary>Nearest-to-camera face whose projected loop contains the mouse, tested by fan-
    /// triangulating the loop and point-in-triangle testing each fan segment — the same
    /// triangulation <see cref="DrawFaceFan"/> renders with.</summary>
    private bool TryPickFace(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin, out int id)
    {
        id = 0;
        NVector2 mouse = ImGui.GetMousePos();
        float bestDistance = float.MaxValue;
        foreach (NetworkFace face in component.Network.Faces)
        {
            if (!TryProjectFace(component, entity, camera, imageMin, face, out NVector2[] screen) ||
                !PointInFan(screen, mouse))
            {
                continue;
            }

            if (FaceCentroid(component, face) is not { } centroid)
            {
                continue;
            }

            float distance = (Display(component, entity, centroid) - camera.GlobalPosition).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                id = face.Id;
            }
        }

        return id != 0;
    }

    /// <summary>Projects every vertex of a face's loop, failing entirely if any vertex is behind the
    /// camera — a partially-projected face has no sensible screen-space fill.</summary>
    private bool TryProjectFace(INetworkEditable component, SceneEntity entity, Camera3D camera, NVector2 imageMin, NetworkFace face, out NVector2[] screen)
    {
        screen = new NVector2[face.Vertices.Count];
        for (int i = 0; i < face.Vertices.Count; i++)
        {
            if (component.Network.Vertex(face.Vertices[i]) is not { } vertex ||
                !Project(camera, imageMin, Display(component, entity, vertex.Position), out screen[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void DrawFaceFan(ImDrawListPtr drawList, NVector2[] screen, uint color)
    {
        for (int i = 1; i < screen.Length - 1; i++)
        {
            drawList.AddTriangleFilled(screen[0], screen[i], screen[i + 1], color);
        }
    }

    private static bool PointInFan(NVector2[] screen, NVector2 point)
    {
        for (int i = 1; i < screen.Length - 1; i++)
        {
            if (PointInTriangle(point, screen[0], screen[i], screen[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointInTriangle(NVector2 p, NVector2 a, NVector2 b, NVector2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);
        bool hasNegative = d1 < 0.0f || d2 < 0.0f || d3 < 0.0f;
        bool hasPositive = d1 > 0.0f || d2 > 0.0f || d3 > 0.0f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(NVector2 a, NVector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Walks a selected set of edges and returns their vertices in loop order if — and only
    /// if — they form exactly one simple closed cycle (every touched vertex has exactly two of the
    /// selected edges, and following them visits every selected edge exactly once). Any other shape
    /// (an open chain, a branch, more than one loop) fails rather than guessing an order.</summary>
    private static IReadOnlyList<int>? TryComputeEdgeLoop(VertexNetwork network, IReadOnlySet<int> edgeIds)
    {
        var edges = edgeIds.Select(network.Edge).OfType<NetworkEdge>().ToList();
        if (edges.Count < 3 || edges.Count != edgeIds.Count)
        {
            return null;
        }

        var adjacency = new Dictionary<int, List<int>>();
        foreach (NetworkEdge edge in edges)
        {
            AddAdjacency(adjacency, edge.A, edge.B);
            AddAdjacency(adjacency, edge.B, edge.A);
        }

        if (adjacency.Values.Any(neighbours => neighbours.Count != 2))
        {
            return null;
        }

        var loop = new List<int>();
        int start = adjacency.Keys.First();
        int previous = -1;
        int current = start;
        do
        {
            loop.Add(current);
            int next = adjacency[current][0] == previous ? adjacency[current][1] : adjacency[current][0];
            previous = current;
            current = next;
        }
        while (current != start && loop.Count <= adjacency.Count);

        return current == start && loop.Count == adjacency.Count ? loop : null;
    }

    private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out List<int>? neighbours))
        {
            neighbours = [];
            adjacency[from] = neighbours;
        }

        neighbours.Add(to);
    }

    /// <summary>Where a new vertex lands for a Ctrl+click. Planar networks drop it onto the terrain
    /// under the cursor (falling back to the world Y=0 plane where no terrain is loaded); others use
    /// the entity's own local Y=0 plane, so a procedural mesh can still be built above or below it.</summary>
    private bool TryPlacementPoint(INetworkEditable component, SceneEntity entity, in ViewportContext viewport, out GVector3 local)
    {
        NVector2 mouse = ImGui.GetMousePos();
        GVector2 inViewport = new(mouse.X - viewport.ImageMin.X, mouse.Y - viewport.ImageMin.Y);
        GVector3 origin = viewport.Camera.ProjectRayOrigin(inViewport);
        GVector3 dir = viewport.Camera.ProjectRayNormal(inViewport);

        if (component.PlanarXZ)
        {
            if (!_terrain.TryHit(origin, dir, out GVector3 world) && !TerrainProbe.TryGroundPlane(origin, dir, out world))
            {
                local = default;
                return false;
            }

            local = entity.Transform.AffineInverse() * world;
            local.Y = 0.0f;
            return true;
        }

        GVector3 planeNormal = entity.Transform.Basis.Y.Normalized();
        float denom = dir.Dot(planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            local = default;
            return false;
        }

        GVector3 planeWorld = origin + dir * ((entity.Transform.Origin - origin).Dot(planeNormal) / denom);
        local = entity.Transform.AffineInverse() * planeWorld;
        return true;
    }

    private void Mutate(INetworkEditable component, string description, Action<VertexNetwork> mutate)
    {
        VertexNetwork before = component.Network.Clone();
        VertexNetwork after = component.Network.Clone();
        mutate(after);
        if (before.Fingerprint() == after.Fingerprint())
        {
            return;
        }

        var command = new SetNetworkCommand(component, before, after, description);
        command.Apply();
        _context.Sessions.Record(command);
        if (component.Owner is { } owner)
        {
            _context.Scene.Touch(owner);
        }
    }

    private void PreviewMutate(INetworkEditable component, Action<VertexNetwork> mutate)
    {
        VertexNetwork after = component.Network.Clone();
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

    private GVector3 PlaneTranslate(Camera3D camera, NVector2 mouse, NVector2 imageMin, GVector3 pivot, GVector3 normal) =>
        RayToPlane(camera, mouse, imageMin, pivot, normal) - RayToPlane(camera, _modalStartMouse, imageMin, pivot, normal);

    private static Transform3D ScaleAround(GVector3 pivot, float factor)
    {
        Basis basis = Basis.Identity;
        basis.X *= factor;
        basis.Y *= factor;
        basis.Z *= factor;
        return new Transform3D(basis, pivot - basis * pivot);
    }

    private static Transform3D ScaleAround(GVector3 pivot, GVector3 axis, float factor)
    {
        Basis basis = Basis.Identity;
        basis.X = ScaleVector(basis.X, axis, factor);
        basis.Y = ScaleVector(basis.Y, axis, factor);
        basis.Z = ScaleVector(basis.Z, axis, factor);
        return new Transform3D(basis, pivot - basis * pivot);
    }

    private static Transform3D ScaleAroundExcludingAxis(GVector3 pivot, GVector3 axis, float factor)
    {
        Basis basis = Basis.Identity;
        basis.X = ScaleVectorExcludingAxis(basis.X, axis, factor);
        basis.Y = ScaleVectorExcludingAxis(basis.Y, axis, factor);
        basis.Z = ScaleVectorExcludingAxis(basis.Z, axis, factor);
        return new Transform3D(basis, pivot - basis * pivot);
    }

    private static GVector3 ScaleVector(GVector3 value, GVector3 axis, float factor) =>
        value + axis * (value.Dot(axis) * (factor - 1.0f));

    private static GVector3 ScaleVectorExcludingAxis(GVector3 value, GVector3 axis, float factor)
    {
        GVector3 alongAxis = axis * value.Dot(axis);
        return alongAxis + (value - alongAxis) * factor;
    }

    private static float ScaleFactorFromScreen(Camera3D camera, NVector2 imageMin, GVector3 pivot, NVector2 startMouse, NVector2 mouse)
    {
        if (!Project(camera, imageMin, pivot, out NVector2 centre))
        {
            return Mathf.Exp((startMouse.Y - mouse.Y) / 120.0f);
        }

        float startDistance = Mathf.Max(8.0f, (startMouse - centre).Length());
        float currentDistance = (mouse - centre).Length();
        return currentDistance / startDistance;
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
