using System;
using System.Collections.Generic;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// An ImGui window that renders a Godot 3D <see cref="SubViewport"/> containing an
/// infinite, camera-following grid, similar to an empty Blender scene.
///
/// Navigation mirrors the Unreal editor viewport: hold the right mouse button to fly.
/// While held, moving the mouse looks around, W/A/S/D moves, E/Q rises and falls,
/// Shift boosts, and the wheel changes fly speed. The cursor is hidden and pinned in
/// place for the duration, so it never physically moves or snags on other UI.
/// </summary>
public sealed class ViewportWindow : ImGuiWindow
{
    private const float LookSensitivity = 0.0025f;
    private const float MinPitch = -1.55f;
    private const float MaxPitch = 1.55f;
    private const float MinFlySpeed = 0.5f;
    private const float MaxFlySpeed = 200.0f;
    private const float BoostMultiplier = 4.0f;

    private const float MarqueeThreshold = 5.0f;

    private readonly Node _owner;
    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly MeshInstance3D _grid;

    // Example objects: click to select, shift-click to add/remove, drag a box to marquee-select.
    private static readonly GVector3 DemoObjectSize = new(2.0f, 2.0f, 2.0f);
    private readonly List<Node3D> _objects = [];
    private readonly List<Node3D> _selection = [];
    private readonly TransformGizmo _gizmo = new();
    private bool _localSpacePreferred = true;

    // The gizmo drives this shared pivot; the resulting delta is applied to every selection.
    private Transform3D _pivot = Transform3D.Identity;
    private Transform3D _dragStartPivot;
    private readonly List<Transform3D> _dragStartTransforms = [];

    // Box (marquee) selection state.
    private bool _mouseDown;
    private bool _marquee;
    private NVector2 _marqueeStart;

    private GVector3 _position = new(8.0f, 6.0f, 8.0f);
    private float _yaw;
    private float _pitch;
    private float _flySpeed = 8.0f;
    private bool _flying;
    private Vector2I _flyAnchor;

    public ViewportWindow(Node owner) : base("Viewport", defaultSize: new NVector2(720, 480))
    {
        _owner = owner;

        _viewport = new SubViewport
        {
            Name = "ViewportWindowSubViewport",
            Size = new Vector2I(720, 480),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
        };

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.28f, 0.28f, 0.28f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.28f, 0.28f, 0.28f),
        };

        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Near = 0.05f,
            Far = 5000.0f,
            Environment = environment,
        };

        var shader = GD.Load<Shader>("res://src/Window/Viewport/InfiniteGrid.gdshader");
        _grid = new MeshInstance3D
        {
            Name = "Grid",
            Mesh = new PlaneMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = shader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_grid);
        CreateDemoScene();
        owner.AddChild(_viewport);

        LookAt(GVector3.Zero);
        ApplyCameraTransform();
    }

    // A handful of lit boxes scattered on the grid, some rotated so local vs. world space
    // is visible. Picking is analytic (ray vs. box), so no physics body is needed.
    private void CreateDemoScene()
    {
        AddBox(new GVector3(-6.0f, 1.0f, -2.0f), new Color(0.85f, 0.55f, 0.28f), 0.0f);
        AddBox(new GVector3(0.0f, 1.0f, 0.0f), new Color(0.45f, 0.70f, 0.85f), 0.6f);
        AddBox(new GVector3(5.0f, 1.0f, 2.0f), new Color(0.60f, 0.80f, 0.45f), -0.3f);
        AddBox(new GVector3(3.0f, 1.0f, -5.0f), new Color(0.82f, 0.45f, 0.62f), 1.1f);
        AddBox(new GVector3(-3.0f, 1.0f, 5.0f), new Color(0.80f, 0.75f, 0.35f), 0.0f);
    }

    private void AddBox(GVector3 position, Color color, float yaw)
    {
        var box = new Node3D
        {
            Name = $"Box{_objects.Count}",
            Transform = new Transform3D(new Basis(GVector3.Up, yaw), position),
        };

        box.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = DemoObjectSize },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                Metallic = 0.2f,
                Roughness = 0.55f,
            },
        });

        _viewport.AddChild(box);
        _objects.Add(box);
    }

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void DrawContent()
    {
        DrawToolbar();

        NVector2 region = ImGui.GetContentRegionAvail();
        int width = Math.Max(1, (int)region.X);
        int height = Math.Max(1, (int)region.Y);
        if (_viewport.Size.X != width || _viewport.Size.Y != height)
        {
            _viewport.Size = new Vector2I(width, height);
        }

        IntPtr textureId = (IntPtr)_viewport.GetTexture().GetRid().Id;
        ImGui.Image(textureId, new NVector2(width, height));

        bool hovered = ImGui.IsItemHovered();
        NVector2 imageMin = ImGui.GetItemRectMin();
        NVector2 imageSize = new(width, height);

        // The gizmo and marquee own the left mouse button, so the fly camera (right mouse)
        // only starts when neither is in play.
        UpdateFlyCamera(hovered && !GizmoBusy && !_mouseDown);

        ApplyCameraTransform();

        // Keep the grid plane centred under the camera so the grid feels endless.
        _grid.GlobalPosition = new GVector3(_position.X, 0.0f, _position.Z);

        UpdateSelectionAndGizmo(hovered, imageMin, imageSize);
    }

    // Toolbar across the top of the viewport: gizmo mode and coordinate space.
    private void DrawToolbar()
    {
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
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        // Local space is only meaningful for a single object; multi-selection forces world.
        bool localAvailable = _selection.Count == 1;
        if (!localAvailable)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.RadioButton("Local", _localSpacePreferred))
        {
            _localSpacePreferred = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("World", !_localSpacePreferred))
        {
            _localSpacePreferred = false;
        }

        if (!localAvailable)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled($"{_selection.Count} selected");
    }

    private void UpdateSelectionAndGizmo(bool hovered, NVector2 imageMin, NVector2 imageSize)
    {
        // W / E toggle between translate and rotate, mirroring the Unreal editor.
        if (hovered && !_flying)
        {
            if (Godot.Input.IsPhysicalKeyPressed(Key.W)) { _gizmo.Operation = GizmoOperation.Translate; }
            if (Godot.Input.IsPhysicalKeyPressed(Key.E)) { _gizmo.Operation = GizmoOperation.Rotate; }
        }

        DrawSelectionOutlines(imageMin);
        DriveGizmo(hovered, imageMin, imageSize);
        HandleSelectionInput(hovered, imageMin, imageSize);
    }

    // Wireframe outline around each selected object, projected from its oriented box.
    private void DrawSelectionOutlines(NVector2 imageMin)
    {
        // 12 edges of a box as index pairs into the 8 corners below.
        ReadOnlySpan<int> edges =
        [
            0, 1, 1, 3, 3, 2, 2, 0, // bottom
            4, 5, 5, 7, 7, 6, 6, 4, // top
            0, 4, 1, 5, 2, 6, 3, 7, // verticals
        ];

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(new NVector4(1.0f, 0.62f, 0.20f, 0.95f));
        GVector3 half = DemoObjectSize * 0.5f;
        Span<NVector2> corners = stackalloc NVector2[8];

        foreach (Node3D obj in _selection)
        {
            Transform3D xform = obj.GlobalTransform;
            bool allVisible = true;
            for (int c = 0; c < 8; c++)
            {
                GVector3 local = new((c & 1) == 0 ? -half.X : half.X,
                                     (c & 4) == 0 ? -half.Y : half.Y,
                                     (c & 2) == 0 ? -half.Z : half.Z);
                if (!WorldToScreen(xform * local, imageMin, out corners[c]))
                {
                    allVisible = false;
                    break;
                }
            }

            if (!allVisible)
            {
                continue;
            }

            for (int e = 0; e < edges.Length; e += 2)
            {
                drawList.AddLine(corners[edges[e]], corners[edges[e + 1]], color, 1.5f);
            }
        }
    }

    // Places the gizmo at the centre of the selection and forwards its motion to every
    // selected object. Local space applies only when exactly one object is selected.
    private void DriveGizmo(bool hovered, NVector2 imageMin, NVector2 imageSize)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        bool useLocal = _localSpacePreferred && _selection.Count == 1;
        _gizmo.LocalSpace = useLocal;

        // While not dragging, keep the pivot pinned to the selection's centre.
        if (!_gizmo.IsUsing)
        {
            _pivot = ComputePivot(useLocal);
        }

        bool wasUsing = _gizmo.IsUsing;
        Transform3D pivotBefore = _pivot;
        bool interactive = hovered && !_flying && !_mouseDown;
        _gizmo.Manipulate(_camera, imageMin, imageSize, interactive, ref _pivot);

        if (_gizmo.IsUsing && !wasUsing)
        {
            // Drag just started: snapshot the pivot and every object so we can apply the
            // total delta each frame (drift-free, unlike accumulating per-frame deltas).
            _dragStartPivot = pivotBefore;
            _dragStartTransforms.Clear();
            foreach (Node3D obj in _selection)
            {
                _dragStartTransforms.Add(obj.GlobalTransform);
            }
        }

        if (_gizmo.IsUsing)
        {
            Transform3D delta = _pivot * _dragStartPivot.AffineInverse();
            for (int i = 0; i < _selection.Count; i++)
            {
                _selection[i].GlobalTransform = delta * _dragStartTransforms[i];
            }
        }
    }

    private Transform3D ComputePivot(bool useLocal)
    {
        GVector3 centre = GVector3.Zero;
        foreach (Node3D obj in _selection)
        {
            centre += obj.GlobalTransform.Origin;
        }

        centre /= _selection.Count;

        Basis basis = useLocal ? _selection[0].GlobalTransform.Basis.Orthonormalized() : Basis.Identity;
        return new Transform3D(basis, centre);
    }

    // ---- Selection input (click, shift-click, marquee) ---------------------

    // The gizmo only claims the mouse when something is selected; guarding on the selection
    // count keeps stale hover/use state from ever locking out fly and selection input.
    private bool GizmoBusy => _selection.Count > 0 && (_gizmo.IsUsing || _gizmo.IsHovered);

    private void HandleSelectionInput(bool hovered, NVector2 imageMin, NVector2 imageSize)
    {
        NVector2 mouse = ImGui.GetMousePos();

        // A fresh press that misses the gizmo starts a potential click / marquee.
        if (!_mouseDown && hovered && !_flying && !GizmoBusy &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _mouseDown = true;
            _marquee = false;
            _marqueeStart = mouse;
        }

        if (!_mouseDown)
        {
            return;
        }

        // Track the button by its level, not the released edge. If a release event is ever
        // missed (e.g. it happens over another window or on a frame this window isn't drawn),
        // the next frame with the button up still finalises and clears the state, so the
        // viewport can never get stuck ignoring input.
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
        bool additive = Godot.Input.IsPhysicalKeyPressed(Key.Shift);
        if (_marquee)
        {
            ApplyBoxSelection(_marqueeStart, mouse, imageMin, imageSize, additive);
        }
        else
        {
            ApplyClickSelection(mouse, imageMin, imageSize, additive);
        }

        _marquee = false;
    }

    private void ApplyClickSelection(NVector2 mouse, NVector2 imageMin, NVector2 imageSize, bool additive)
    {
        Node3D hit = Pick(mouse, imageMin, imageSize);
        if (hit != null)
        {
            if (additive)
            {
                if (!_selection.Remove(hit))
                {
                    _selection.Add(hit);
                }
            }
            else
            {
                _selection.Clear();
                _selection.Add(hit);
            }
        }
        else if (!additive)
        {
            _selection.Clear();
        }
    }

    private void ApplyBoxSelection(NVector2 a, NVector2 b, NVector2 imageMin, NVector2 imageSize, bool additive)
    {
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        if (!additive)
        {
            _selection.Clear();
        }

        foreach (Node3D obj in _objects)
        {
            if (!WorldToScreen(obj.GlobalTransform.Origin, imageMin, out NVector2 screen))
            {
                continue;
            }

            bool inside = screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y;
            if (inside && !_selection.Contains(obj))
            {
                _selection.Add(obj);
            }
        }
    }

    // Nearest object under the cursor, or null. Uses an analytic ray-vs-box slab test.
    private Node3D Pick(NVector2 mouse, NVector2 imageMin, NVector2 imageSize)
    {
        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        if (local.X < 0.0f || local.Y < 0.0f || local.X > imageSize.X || local.Y > imageSize.Y)
        {
            return null;
        }

        GVector3 from = _camera.ProjectRayOrigin(local);
        GVector3 dir = _camera.ProjectRayNormal(local);

        Node3D best = null;
        float bestT = float.PositiveInfinity;
        foreach (Node3D obj in _objects)
        {
            if (TryRayBox(from, dir, obj.GlobalTransform, DemoObjectSize, out float t) && t < bestT)
            {
                bestT = t;
                best = obj;
            }
        }

        return best;
    }

    private bool WorldToScreen(GVector3 world, NVector2 imageMin, out NVector2 screen)
    {
        if (_camera.IsPositionBehind(world))
        {
            screen = default;
            return false;
        }

        GVector2 p = _camera.UnprojectPosition(world);
        screen = new NVector2(imageMin.X + p.X, imageMin.Y + p.Y);
        return true;
    }

    private static void DrawMarquee(NVector2 a, NVector2 b)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new NVector4(0.30f, 0.55f, 0.95f, 0.20f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new NVector4(0.40f, 0.65f, 1.0f, 0.90f)));
    }

    // Slab test: transform the ray into the box's local space and clip against its extents.
    // Reports the entry distance so the caller can pick the nearest hit.
    private static bool TryRayBox(GVector3 origin, GVector3 dir, Transform3D boxTransform, GVector3 size, out float tHit)
    {
        tHit = 0.0f;
        Transform3D inv = boxTransform.AffineInverse();
        GVector3 o = inv * origin;
        GVector3 d = inv.Basis * dir;
        GVector3 half = size * 0.5f;

        float[] oc = [o.X, o.Y, o.Z];
        float[] dc = [d.X, d.Y, d.Z];
        float[] hc = [half.X, half.Y, half.Z];

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(dc[a]) < 1e-8f)
            {
                if (oc[a] < -hc[a] || oc[a] > hc[a])
                {
                    return false;
                }

                continue;
            }

            float t1 = (-hc[a] - oc[a]) / dc[a];
            float t2 = (hc[a] - oc[a]) / dc[a];
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

    private void UpdateFlyCamera(bool hovered)
    {
        // Right mouse button engages fly mode, exactly like the Unreal viewport.
        if (!_flying && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            BeginFly();
        }
        else if (_flying && !Godot.Input.IsMouseButtonPressed(MouseButton.Right))
        {
            EndFly();
        }

        if (!_flying)
        {
            return;
        }

        // Mouselook: read how far the cursor moved from the anchor, then snap it back
        // so the OS cursor never actually travels and can't get stuck on anything.
        Vector2I mouse = GetMousePixels();
        Vector2I delta = mouse - _flyAnchor;
        _yaw -= delta.X * LookSensitivity;
        _pitch = Mathf.Clamp(_pitch - delta.Y * LookSensitivity, MinPitch, MaxPitch);
        Godot.Input.WarpMouse(_flyAnchor);

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.MouseWheel != 0.0f)
        {
            _flySpeed = Mathf.Clamp(_flySpeed * Mathf.Pow(1.15f, io.MouseWheel), MinFlySpeed, MaxFlySpeed);
        }

        MoveFromKeyboard(io.DeltaTime);
    }

    private void MoveFromKeyboard(float delta)
    {
        float cosPitch = Mathf.Cos(_pitch);
        GVector3 forward = new(-Mathf.Sin(_yaw) * cosPitch, Mathf.Sin(_pitch), -Mathf.Cos(_yaw) * cosPitch);
        GVector3 right = new(Mathf.Cos(_yaw), 0.0f, -Mathf.Sin(_yaw));

        GVector3 direction = GVector3.Zero;
        if (Godot.Input.IsPhysicalKeyPressed(Key.W)) { direction += forward; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.S)) { direction -= forward; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.D)) { direction += right; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.A)) { direction -= right; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.E)) { direction += GVector3.Up; }
        if (Godot.Input.IsPhysicalKeyPressed(Key.Q)) { direction -= GVector3.Up; }

        if (direction == GVector3.Zero)
        {
            return;
        }

        float speed = _flySpeed;
        if (Godot.Input.IsPhysicalKeyPressed(Key.Shift))
        {
            speed *= BoostMultiplier;
        }

        _position += direction.Normalized() * speed * delta;
    }

    private void BeginFly()
    {
        _flying = true;
        _flyAnchor = GetMousePixels();
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.ConfinedHidden;
    }

    private void EndFly()
    {
        _flying = false;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
        Godot.Input.WarpMouse(_flyAnchor);
    }

    private Vector2I GetMousePixels()
    {
        return DisplayServer.MouseGetPosition() - _owner.GetWindow().Position;
    }

    private void LookAt(GVector3 target)
    {
        GVector3 direction = (target - _position).Normalized();
        _pitch = Mathf.Clamp(Mathf.Asin(direction.Y), MinPitch, MaxPitch);
        _yaw = Mathf.Atan2(-direction.X, -direction.Z);
    }

    private void ApplyCameraTransform()
    {
        _camera.Position = _position;
        _camera.Rotation = new GVector3(_pitch, _yaw, 0.0f);
    }
}
