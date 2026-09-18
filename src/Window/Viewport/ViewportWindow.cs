using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using ImGuiNET;
using GVector2 = Godot.Vector2;
using GVector3 = Godot.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// An ImGui window that renders a Godot 3D <see cref="SubViewport"/> with an infinite,
/// camera-following grid, similar to an empty Blender scene. It owns the camera, the
/// <see cref="FlyCamera"/> navigation and the demo scene, and each frame hands viewport
/// interaction to the active <see cref="ITool"/> from the shared <see cref="ToolSystem"/>.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed partial class ViewportWindow : Window, IWorldParticipant, ILayoutPersistentWindow, ISubsystemHost
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.V, ShortcutModifiers.Alt);

    // Dim red/green/blue for the grid's axis lines, indexed by *user* axis (0 = X, 1 = Y, 2 = Z)
    // so the line for whichever Godot axis a user axis is mapped onto always reads as that color.
    private static readonly Color[] UserAxisLineColors =
    [
        new Color(0.55f, 0.18f, 0.18f), // user X - red
        new Color(0.18f, 0.5f, 0.18f),  // user Y - green
        new Color(0.18f, 0.32f, 0.6f),  // user Z - blue
    ];

    private static readonly GVector3 DefaultCameraPosition = new(8.0f, 6.0f, 8.0f);

    /// <summary>How far back the camera sits when asked to look at something.</summary>
    private const float FocusDistance = 40.0f;

    /// <summary>Grid cells across one landscape chunk, so major lines fall on chunk boundaries.</summary>
    private const int GridCellsPerChunk = 8;

    /// <summary>How far the grid plane sits below y=0, to keep it out of flat terrain's depth.</summary>
    private const float GridDepthOffset = -0.1f;

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly MeshInstance3D _grid;
    private readonly MeshInstance3D _upAxisLine;
    private readonly AxisConvention _axes;

    private readonly FlyCamera _flyCamera;
    private readonly ViewSettings _view;
    private readonly SceneEntityRegistry _scene;
    private readonly ViewCategorySystem _viewCategories;
    private readonly ToolSystem _tools;
    private readonly StreamingSystem _streaming;
    private readonly EnvironmentSystem _environments;
    private readonly EnvironmentRenderer _environmentRenderer;
    private readonly EnvironmentVolumeGizmos _environmentVolumes;
    private readonly LandscapeSystem _landscape;
    private readonly MapSystem _maps;
    private readonly TerrainProbe _terrainProbe;
    private readonly ViewportPointer _pointer;
    private readonly ViewportHeader _header;
    private readonly HashSet<SceneEntity> _represented = [];

    // The scene/filter versions _represented was last brought in step with; -1 until each has been.
    private int _representedVersion = -1;
    private int _representedFilterVersion = -1;

    // Building a Godot node per newly-in-view entity is capped at a time budget per frame, with the
    // rest carried here: a large scan brings hundreds in at once, and doing them all on one frame is
    // exactly the stall that slows streaming (which advances one step per frame).
    private const double RepresentationBudgetMs = 4.0;
    private static readonly System.Diagnostics.Stopwatch RepresentationClock = new();
    private readonly List<SceneEntity> _representationBacklog = [];
    private readonly Dictionary<MapId, GVector3> _cameraByMap = [];

    private MapId _viewMap;
    private (bool ChunkEdges, bool VertexColor, bool VertexLight) _terrainDisplay = (true, true, true);
    private float? _gridScaleChunkSize;
    private (SignedAxis X, SignedAxis Y, SignedAxis Z)? _axisLineColorsFor;

    /// <summary>Lets a subsystem hosted here (an <see cref="IViewportOverlay"/>, so far) reach shared
    /// systems — subsystem constructors only ever receive their direct parent, so a hosted factory
    /// needing broader access exposes its own storage's context the same way.</summary>
    public EditorContext Context { get; }

    public ViewportWindow(WindowManager manager) : base("Viewport", defaultSize: new NVector2(720, 480))
    {
        EditorContext context = manager.Context;
        Context = context;
        Node owner = context.Root;
        _flyCamera = new FlyCamera(owner, DefaultCameraPosition);
        _view = context.View;
        _scene = context.Scene;
        _viewCategories = context.ViewCategories;
        _tools = context.Tools;
        _streaming = context.Streaming;
        _environments = context.Environments;
        _landscape = context.Landscape;
        _maps = context.Maps;
        _viewMap = _maps.CurrentMap;
        _axes = context.Axes;
        _terrainProbe = new TerrainProbe(_landscape);
        _pointer = context.Pointer;
        _header = new ViewportHeader(context);

        _viewport = new SubViewport
        {
            Name = "ViewportWindowSubViewport",
            Size = new Vector2I(720, 480),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
        };

        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Near = 0.05f,
            Far = 5000.0f,
        };

        var gridShader = GD.Load<Shader>("res://src/Window/Viewport/InfiniteGrid.gdshader");
        _grid = new MeshInstance3D
        {
            Name = "Grid",
            Mesh = new PlaneMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = gridShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        // Completes the grid's X/Z axis lines with a vertical Y line through the origin, drawn
        // on a quad kept facing the camera each frame (see DrawContent) so it reads as a crisp
        // line from any angle, mirroring the grid's own axis line rendering.
        var axisLineShader = GD.Load<Shader>("res://src/Window/Viewport/AxisLine.gdshader");
        _upAxisLine = new MeshInstance3D
        {
            Name = "UpAxisLine",
            Mesh = new QuadMesh { Size = new GVector2(4000.0f, 4000.0f) },
            MaterialOverride = new ShaderMaterial { Shader = axisLineShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _viewport.AddChild(_camera);
        _viewport.AddChild(_grid);
        _viewport.AddChild(_upAxisLine);
        owner.AddChild(_viewport);

        _environmentRenderer = new EnvironmentRenderer(_viewport, _camera, context.Assets, context.MeshMaterials, _environments, _viewCategories);
        _camera.Environment = _environmentRenderer.Environment;
        _environmentVolumes = new EnvironmentVolumeGizmos(_viewport, _viewCategories, _view, context.Selection);

        // The map picker previews each map with a snapshot of this view; only the viewport can take one.
        _maps.CaptureView = () => _viewport.GetTexture()?.GetImage();

        // Anything that can point at a place in the world — the Problems window, say — asks here.
        context.Focus.Handler = LookAt;
        context.Focus.PositionGetter = () => _flyCamera.Position;
        context.Focus.PositionSetter = _flyCamera.MoveTo;

        _flyCamera.LookAt(GVector3.Zero);
        _flyCamera.ApplyTo(_camera);

        InitializeSubsystems();
    }

    /// <summary>Every self-registered <see cref="IViewportOverlay"/>, drawn once per frame in
    /// <see cref="DrawContent"/> — see that interface's own doc comment for when.</summary>
    private IEnumerable<IViewportOverlay> Overlays => Subsystems.OfType<IViewportOverlay>();

    // Backs off along the current view direction so the target is framed rather than sat inside.
    private void LookAt(GVector3 target)
    {
        GVector3 back = (_flyCamera.Position - target);
        GVector3 direction = back.LengthSquared() > 0.001f ? back.Normalized() : new GVector3(0.0f, 0.5f, 1.0f).Normalized();

        _flyCamera.MoveTo(target + (direction * FocusDistance));
        _flyCamera.LookAt(target);
    }

    // Creates a viewport representation for every loaded entity that lacks one, and tears down the
    // representation of any entity that has left the registry (e.g. an undone creation).
    private void SyncRepresentations()
    {
        // Every answer below — what is in view, what is still registered, what turned peripheral, what
        // the view filter hides — comes from state that bumps its own version when it changes, so
        // between bumps this pass can only reach the same conclusions it reached last frame. Skipping
        // it matters because the removal sweep is over everything represented, which at a real view
        // distance is the whole loaded world once per frame.
        // A backlog still draining is a frame with work to do even when neither version has moved.
        bool sceneChanged = _representedVersion != _scene.Version;
        bool filterChanged = _representedFilterVersion != _viewCategories.Version;
        if (!sceneChanged && !filterChanged && _representationBacklog.Count == 0)
        {
            return;
        }

        if (sceneChanged)
        {
            _representedVersion = _scene.Version;

            _representationBacklog.Clear();
            foreach (SceneEntity entity in _viewCategories.Visible)
            {
                if (!_represented.Contains(entity))
                {
                    _representationBacklog.Add(entity);
                }
            }

            // Drops entities that left the registry *and* ones that became peripheral: those are
            // loaded only to shape the terrain, and drawing them would put scenery beyond where you
            // can go. Runs on the version change, not per frame.
            _represented.RemoveWhere(entity =>
            {
                if (_scene.Contains(entity) && !_scene.IsPeripheral(entity))
                {
                    return false;
                }

                entity.DestroyRepresentation();
                return true;
            });
        }

        if (filterChanged)
        {
            _representedFilterVersion = _viewCategories.Version;

            // Already represented: flip the mirrored flag in place, never rebuild.
            foreach (SceneEntity entity in _represented)
            {
                entity.Visible = !_viewCategories.IsHidden(entity);
            }

            // Not yet represented and newly un-hidden: enters the ordinary budgeted backlog, same as
            // any other entity newly in view.
            foreach (SceneEntity entity in _viewCategories.Visible)
            {
                if (!_represented.Contains(entity) && !_representationBacklog.Contains(entity))
                {
                    _representationBacklog.Add(entity);
                }
            }
        }

        RepresentationClock.Restart();
        int made = 0;
        while (made < _representationBacklog.Count)
        {
            SceneEntity entity = _representationBacklog[made++];
            if (_scene.Contains(entity) && !_scene.IsPeripheral(entity) && !_viewCategories.IsHidden(entity) && _represented.Add(entity))
            {
                entity.CreateRepresentation(_viewport);
            }

            if (RepresentationClock.Elapsed.TotalMilliseconds >= RepresentationBudgetMs)
            {
                break;
            }
        }

        _representationBacklog.RemoveRange(0, made);
    }

    // Destroys the Godot node of every entity this window has represented and forgets the sky/gizmo
    // state derived from them. Explicit rather than left to the per-frame sync in SyncRepresentations:
    // a reload leaves the editor scene entirely while it runs, so this window draws no frames for it
    // to piggyback on.
    void IWorldParticipant.UnloadWorld()
    {
        foreach (SceneEntity entity in _represented)
        {
            entity.DestroyRepresentation();
        }

        _represented.Clear();
        _representationBacklog.Clear();
        _representedVersion = -1;
        _representedFilterVersion = -1;
        _environmentRenderer.Unload();
        _environmentVolumes.Unload();
    }

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    // Each map keeps the camera where the user left it, so switching back and forth doesn't lose your
    // place; a map entered for the first time starts at the default view over its origin.
    private void FollowCurrentMap()
    {
        MapId map = _maps.CurrentMap;
        if (map == _viewMap)
        {
            return;
        }

        _cameraByMap[_viewMap] = _flyCamera.Position;
        _viewMap = map;

        if (_cameraByMap.TryGetValue(map, out GVector3 position))
        {
            _flyCamera.MoveTo(position);
        }
        else
        {
            _flyCamera.MoveTo(DefaultCameraPosition);
            _flyCamera.LookAt(GVector3.Zero);
        }
    }

    // The live camera pose plus every per-map position the session has accumulated, so restarting
    // drops you back exactly where you left off on each map rather than at the default overview.
    JsonObject? ILayoutPersistentWindow.CaptureLayoutState()
    {
        _cameraByMap[_viewMap] = _flyCamera.Position;

        JsonObject perMap = new();
        foreach ((MapId map, GVector3 position) in _cameraByMap.OrderBy(pair => pair.Key.Value))
        {
            perMap[map.Value.ToString()] = new JsonObject
            {
                ["x"] = position.X,
                ["y"] = position.Y,
                ["z"] = position.Z,
            };
        }

        return new JsonObject
        {
            ["pose"] = _flyCamera.Pose.ToJson(),
            ["perMap"] = perMap,
        };
    }

    void ILayoutPersistentWindow.RestoreLayoutState(JsonObject state)
    {
        if (state["perMap"] is JsonObject perMap)
        {
            _cameraByMap.Clear();
            foreach ((string key, JsonNode? value) in perMap)
            {
                if (value is JsonObject position && int.TryParse(key, out int mapValue))
                {
                    _cameraByMap[new MapId(mapValue)] = new GVector3(
                        Coord(position, "x"), Coord(position, "y"), Coord(position, "z"));
                }
            }
        }

        if (CameraPose.FromJson(state["pose"]) is { } pose)
        {
            _flyCamera.SetPose(pose);
            _flyCamera.ApplyTo(_camera);
        }

        static float Coord(JsonObject obj, string key) =>
            obj.TryGetPropertyValue(key, out JsonNode? value) && value is not null ? value.GetValue<float>() : 0.0f;
    }

    protected override void DrawContent()
    {
        FollowCurrentMap();
        _landscape.Focus = _flyCamera.Position;
        _streaming.Update(_flyCamera.Position);
        _environments.Update(_flyCamera.Position);
        _environmentRenderer.Update(_flyCamera.Position);
        SyncRepresentations();
        _environmentVolumes.Update();

        ITool? tool = _tools.Active;
        tool?.DrawToolbar();
        _header.Draw(new ViewportHeaderContext(_camera, _flyCamera.IsFlying), continueRow: tool is not null);

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

        // The active tool owns the mouse buttons while interacting, so the fly camera
        // (right mouse) only starts when the tool isn't capturing.
        _flyCamera.Update(hovered && !(tool?.CapturesMouse ?? false));
        _flyCamera.ApplyTo(_camera);
        FramePick pick = UpdatePointer(hovered, imageMin);

        _grid.Visible = _view.ShowGrid;
        _upAxisLine.Visible = _view.ShowGrid;
        UpdateGridScale();
        UpdateTerrainDisplay();

        // Centred under the camera so the grid feels endless, and a hair below the ground plane:
        // flat terrain sits at exactly zero, and two coplanar surfaces fight for depth.
        _grid.GlobalPosition = new GVector3(_flyCamera.Position.X, GridDepthOffset, _flyCamera.Position.Z);

        // The up axis line stays pinned to the true X=0/Z=0 column (that's the line it draws),
        // only following the camera vertically, and is kept yawed to face the camera so its
        // screen-space line rendering stays correct from any angle.
        _upAxisLine.GlobalPosition = new GVector3(0.0f, _flyCamera.Position.Y, 0.0f);
        GVector3 lookTarget = new(_camera.GlobalPosition.X, _upAxisLine.GlobalPosition.Y, _camera.GlobalPosition.Z);
        if ((lookTarget - _upAxisLine.GlobalPosition).LengthSquared() > 1e-6f)
        {
            _upAxisLine.LookAt(lookTarget, GVector3.Up);
        }

        UpdateAxisLineColors();
        DrawOverlays(imageMin, imageSize);
        tool?.UpdateViewport(new ViewportContext(
            _camera, imageMin, imageSize, hovered, _flyCamera.IsFlying,
            pick.RayOrigin, pick.RayDir, pick.TerrainHit, pick.TerrainPoint));
    }

    // Built once per frame and handed to every registered overlay, so a frame with several of them
    // still pays ViewportProjector's own "two marshalled Godot calls" cost once, not once per overlay.
    private void DrawOverlays(NVector2 imageMin, NVector2 imageSize)
    {
        var projector = new ViewportProjector(_camera, imageMin, imageSize);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        foreach (IViewportOverlay overlay in Overlays)
        {
            overlay.Draw(projector, drawList, _camera);
        }
    }

    // The mouse pick ray for a frame and what it found: shared out to the active tool so a single
    // terrain raycast covers both this window's pointer and the tool that would otherwise re-cast it.
    private readonly record struct FramePick(GVector3 RayOrigin, GVector3 RayDir, bool TerrainHit, GVector3 TerrainPoint);

    // The terrain raycast walks every loaded chunk, so it is skipped on a frame where nothing that
    // could change its result moved: the cursor, the camera, or what is loaded.
    private NVector2 _pointerMouse = new(float.NaN, float.NaN);
    private Transform3D _pointerCamera = new(Basis.Identity, new GVector3(float.NaN, 0.0f, 0.0f));
    private int _pointerSceneVersion = -1;
    private FramePick _pointerPick;

    // Casts a ray from the mouse into the world (terrain, falling back to the Y=0 ground plane) so
    // anything that places something under the cursor — paste, so far — knows where that is without
    // needing its own camera/viewport-rect plumbing.
    private FramePick UpdatePointer(bool hovered, NVector2 imageMin)
    {
        _pointer.Hovered = hovered;
        if (!hovered)
        {
            _pointer.Valid = false;
            return default;
        }

        NVector2 mouse = ImGui.GetMousePos();
        Transform3D camera = _camera.GlobalTransform;
        if (mouse == _pointerMouse && camera == _pointerCamera && _scene.Version == _pointerSceneVersion)
        {
            return _pointerPick;
        }

        _pointerMouse = mouse;
        _pointerCamera = camera;
        _pointerSceneVersion = _scene.Version;

        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        GVector3 origin = _camera.ProjectRayOrigin(local);
        GVector3 dir = _camera.ProjectRayNormal(local);

        bool terrainHit = _terrainProbe.TryHit(origin, dir, out GVector3 terrainPoint);
        if (terrainHit)
        {
            _pointer.Valid = true;
            _pointer.WorldPoint = terrainPoint;
        }
        else if (TerrainProbe.TryGroundPlane(origin, dir, out GVector3 plane))
        {
            _pointer.Valid = true;
            _pointer.WorldPoint = plane;
        }
        else
        {
            _pointer.Valid = false;
        }

        _pointerPick = new FramePick(origin, dir, terrainHit, terrainPoint);
        return _pointerPick;
    }

    // Applies the terrain view toggles to batches already built. New ones pick them up from the
    // static defaults when their material is made.
    private void UpdateTerrainDisplay()
    {
        LandscapeBatchMesh.ShowChunkEdges = _view.ShowChunkEdges;
        LandscapeBatchMesh.ShowVertexColor = _view.ShowTerrainVertexColor;
        LandscapeBatchMesh.ShowVertexLight = _view.ShowTerrainVertexLight;

        var display = (_view.ShowChunkEdges, _view.ShowTerrainVertexColor, _view.ShowTerrainVertexLight);
        if (_terrainDisplay == display)
        {
            return;
        }

        _terrainDisplay = display;
        foreach (SceneEntity entity in _scene.Entities)
        {
            (entity as LandscapeTerrainBatch)?.ApplyDisplayToggles();
        }
    }

    /// <summary>
    /// Matches the grid to the open map's landscape: cells subdivide a chunk, and chunk boundaries
    /// get their own emphasised lines. Where the terrain is actually divided is more useful to see
    /// than where round metric numbers fall, because the texture budget is spent per chunk.
    ///
    /// With no landscape the grid keeps its plain one-unit spacing.
    /// </summary>
    private void UpdateGridScale()
    {
        float chunk = _landscape.Settings is { ChunkWorldSize: > 0.0f } settings ? settings.ChunkWorldSize : 0.0f;

        // Every parameter here is purely a function of the chunk size, which changes only when the
        // landscape's own settings change — not every frame. Re-uploading five shader parameters
        // unconditionally on every DrawContent was a fixed per-frame tax paid regardless of whether
        // anything changed.
        if (_gridScaleChunkSize == chunk)
        {
            return;
        }

        _gridScaleChunkSize = chunk;
        var material = (ShaderMaterial)_grid.MaterialOverride;

        if (chunk <= 0.0f)
        {
            material.SetShaderParameter("chunk_size", 0.0f);
            material.SetShaderParameter("cell_size", 1.0f);
            material.SetShaderParameter("major_every", 10.0f);
            material.SetShaderParameter("fade_start", 18.0f);
            material.SetShaderParameter("fade_end", 90.0f);
            return;
        }

        // Cells divide the chunk rather than the world, so every line is on a boundary of something
        // real and the major lines land exactly on chunk edges.
        material.SetShaderParameter("chunk_size", chunk);
        material.SetShaderParameter("cell_size", chunk / GridCellsPerChunk);
        material.SetShaderParameter("major_every", (float)GridCellsPerChunk);

        // A chunk-sized world needs a chunk-sized fade, or the grid dies out inside one chunk.
        material.SetShaderParameter("fade_start", chunk * 1.5f);
        material.SetShaderParameter("fade_end", chunk * 8.0f);
    }

    // Colors the grid's two horizontal axis lines and the vertical up line by whichever user
    // axis maps onto that Godot spatial axis under the project's AxisConvention, so the lines
    // always read as the user's own X/Y/Z (red/green/blue) no matter how they are remapped.
    private void UpdateAxisLineColors()
    {
        var current = (_axes.X, _axes.Y, _axes.Z);

        // These colors are purely a function of the axis convention, which changes only when the
        // user remaps an axis — not every frame. Same fix as UpdateGridScale: re-uploading shader
        // parameters unconditionally on every DrawContent was a fixed per-frame tax for no reason.
        if (_axisLineColorsFor == current)
        {
            return;
        }

        _axisLineColorsFor = current;

        var gridMaterial = (ShaderMaterial)_grid.MaterialOverride;
        gridMaterial.SetShaderParameter("x_axis_color", ColorForSpatialAxis(0));
        gridMaterial.SetShaderParameter("z_axis_color", ColorForSpatialAxis(2));

        var upLineMaterial = (ShaderMaterial)_upAxisLine.MaterialOverride;
        upLineMaterial.SetShaderParameter("axis_color", ColorForSpatialAxis(1));
    }

    private Color ColorForSpatialAxis(int spatial)
    {
        for (int userAxis = 0; userAxis < 3; userAxis++)
        {
            if (_axes.Get(userAxis).Spatial() == spatial)
            {
                return UserAxisLineColors[userAxis];
            }
        }

        return Colors.White; // Unreachable: the convention is always a full permutation.
    }
}
