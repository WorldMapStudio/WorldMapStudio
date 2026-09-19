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
/// Viewport picking over <see cref="SceneEntity"/>s: left-click selects, shift-click adds or
/// removes, and dragging from empty space marquee-selects everything whose centre falls inside the
/// box. Marquee selection skips <see cref="IDerivedEntity"/>s (e.g. landscape chunks) — they're
/// still individually clickable for their inspector, but a box drag shouldn't sweep them up.
/// Selection lives in the shared <see cref="SelectionSystem"/> so the outline and inspector stay in
/// sync. Marquee selection and the outline both work against <see cref="SceneEntity.LocalBounds"/>
/// in the entity's world transform; click selection prefers ray-testing real geometry where an
/// entity has any — see <see cref="Pick"/>.
/// </summary>
public sealed class ObjectSelection
{
    private const float MarqueeThreshold = 5.0f;

    private readonly SelectionSystem _selection;
    private readonly ViewCategorySystem _viewCategories;
    private readonly SceneEntityRegistry _scene;
    private readonly ObjectTargetFilter _filter;

    private readonly List<SceneEntity> _selectionCache = [];
    private int _cachedVersion = -1;

    // What clicks and marquees may hit: the visible entities the tag filter allows. Held until any of
    // what it derives from moves, so a frame with nothing changed costs nothing.
    private readonly List<SceneEntity> _targetable = [];
    private int _targetableSceneVersion = -1;
    private int _targetableViewVersion = -1;
    private int _targetableTagVersion = -1;
    private int _targetableFilterVersion = -1;

    /// <summary>True while a click or marquee drag is in progress.</summary>
    public bool IsDragging => _mouseDown;

    private bool _mouseDown;
    private bool _marquee;
    private NVector2 _marqueeStart;

    public ObjectSelection(SelectionSystem selection, ViewCategorySystem viewCategories, SceneEntityRegistry scene, ObjectTargetFilter filter)
    {
        _selection = selection;
        _viewCategories = viewCategories;
        _scene = scene;
        _filter = filter;
    }

    /// <summary>The visible entities the Object tool may target: <see cref="ViewCategorySystem.Visible"/>
    /// narrowed by the tag filter.</summary>
    public IReadOnlyList<SceneEntity> Targetable
    {
        get
        {
            if (_targetableSceneVersion == _scene.Version
                && _targetableViewVersion == _viewCategories.Version
                && _targetableTagVersion == _scene.TagVersion
                && _targetableFilterVersion == _filter.Version)
            {
                return _targetable;
            }

            _targetableSceneVersion = _scene.Version;
            _targetableViewVersion = _viewCategories.Version;
            _targetableTagVersion = _scene.TagVersion;
            _targetableFilterVersion = _filter.Version;
            _targetable.Clear();
            foreach (SceneEntity entity in _viewCategories.Visible)
            {
                if (_filter.Targets(entity))
                {
                    _targetable.Add(entity);
                }
            }

            return _targetable;
        }
    }

    /// <summary>The selected scene entities, cached until the shared selection next changes.</summary>
    public IReadOnlyList<SceneEntity> Selection
    {
        get
        {
            if (_cachedVersion != _selection.Version)
            {
                _selectionCache.Clear();
                foreach (IEntity entity in _selection.Selected)
                {
                    if (entity is SceneEntity scene)
                    {
                        _selectionCache.Add(scene);
                    }
                }

                _cachedVersion = _selection.Version;
            }

            return _selectionCache;
        }
    }

    // canStartClick should already fold in "hovered, and no other system (fly camera,
    // gizmo) currently owns the mouse" -- once a drag is under way it continues regardless.
    public void HandleInput(bool canStartClick, Camera3D camera, NVector2 imageMin, NVector2 imageSize)
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
            ApplyBoxSelection(_marqueeStart, mouse, camera, imageMin, additive);
        }
        else
        {
            ApplyClickSelection(mouse, camera, imageMin, imageSize, additive);
        }

        _marquee = false;
    }

    private void ApplyClickSelection(NVector2 mouse, Camera3D camera, NVector2 imageMin, NVector2 imageSize, bool additive)
    {
        SceneEntity? hit = Pick(mouse, camera, imageMin, imageSize);
        if (hit != null)
        {
            if (additive)
            {
                _selection.Toggle(hit);
            }
            else
            {
                _selection.Set(hit);
            }
        }
        else if (!additive)
        {
            _selection.Clear();
        }
    }

    private void ApplyBoxSelection(NVector2 a, NVector2 b, Camera3D camera, NVector2 imageMin, bool additive)
    {
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        if (!additive)
        {
            _selection.Clear();
        }

        foreach (SceneEntity obj in Targetable)
        {
            if (obj is IDerivedEntity)
            {
                continue;
            }

            GVector3 centre = obj.Transform * obj.LocalBounds.GetCenter();
            if (!WorldToScreen(camera, centre, imageMin, out NVector2 screen))
            {
                continue;
            }

            bool inside = screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y;
            if (inside)
            {
                _selection.Add(obj);
            }
        }
    }

    /// <summary>
    /// Nearest entity under the cursor, or null.
    ///
    /// Two phases. The broad phase is a ray-vs-box test over everything in view; it both rejects the
    /// bulk of the scene and yields a lower bound on how far away each survivor's real geometry can
    /// possibly be. The narrow phase then walks the survivors nearest-box-first and, for anything
    /// carrying real geometry, ray-tests its actual triangles — so clicking through the hollow of an
    /// archway or past a model's silhouette correctly misses it. Entities that only draw editor
    /// helpers (markers, stamps) or whose model is still streaming in keep their box hit.
    ///
    /// Sorting is what makes triangle testing affordable: once something is hit at distance t, every
    /// remaining candidate whose box starts beyond t is unreachable, so a click typically tests the
    /// triangles of one or two models rather than every model in view.
    /// </summary>
    private SceneEntity? Pick(NVector2 mouse, Camera3D camera, NVector2 imageMin, NVector2 imageSize)
    {
        GVector2 local = new(mouse.X - imageMin.X, mouse.Y - imageMin.Y);
        if (local.X < 0.0f || local.Y < 0.0f || local.X > imageSize.X || local.Y > imageSize.Y)
        {
            return null;
        }

        GVector3 from = camera.ProjectRayOrigin(local);
        GVector3 dir = camera.ProjectRayNormal(local);

        // (entity, nearest possible distance, distance to use if it has no geometry to test)
        var candidates = new List<(SceneEntity Entity, float Near, float BoxHit)>();
        foreach (SceneEntity obj in Targetable)
        {
            if (TryRayBox(from, dir, obj.Transform, obj.LocalBounds, out float boxHit, out float near))
            {
                candidates.Add((obj, near, boxHit));
            }
        }

        candidates.Sort(static (a, b) => a.Near.CompareTo(b.Near));

        SceneEntity? best = null;
        float bestT = float.PositiveInfinity;
        foreach ((SceneEntity obj, float near, float boxHit) in candidates)
        {
            if (near >= bestT)
            {
                break;
            }

            bool geometryHit = obj.TryPickGeometry(from, dir, out float t, out bool hadGeometry);
            if (hadGeometry)
            {
                if (geometryHit && t < bestT)
                {
                    bestT = t;
                    best = obj;
                }
            }
            else if (boxHit < bestT)
            {
                bestT = boxHit;
                best = obj;
            }
        }

        return best;
    }

    // Wireframe outline around each selected entity, projected from its oriented bounds.
    public void DrawOutlines(Camera3D camera, NVector2 imageMin)
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
        Span<NVector2> corners = stackalloc NVector2[8];

        foreach (SceneEntity obj in Selection)
        {
            Transform3D xform = obj.Transform;
            Aabb bounds = obj.LocalBounds;
            GVector3 lo = bounds.Position;
            GVector3 hi = bounds.End;

            bool allVisible = true;
            for (int c = 0; c < 8; c++)
            {
                GVector3 local = new((c & 1) == 0 ? lo.X : hi.X,
                                     (c & 4) == 0 ? lo.Y : hi.Y,
                                     (c & 2) == 0 ? lo.Z : hi.Z);
                if (!WorldToScreen(camera, xform * local, imageMin, out corners[c]))
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

    /// <summary>
    /// The part of the selection that can actually be moved.
    ///
    /// Derived entities — landscape chunks — stay selectable because their inspector is worth having,
    /// but their content is computed, so dragging one would be editing an output. Filtering them out
    /// here means the gizmo never offers a move that the edit session would then have to refuse.
    /// </summary>
    public IReadOnlyList<SceneEntity> Movable
    {
        get
        {
            var movable = new List<SceneEntity>();
            foreach (SceneEntity entity in Selection)
            {
                if (entity is not IDerivedEntity)
                {
                    movable.Add(entity);
                }
            }

            return movable;
        }
    }

    // Centre of what can be moved and, for local space with exactly one entity, its orientation.
    public Transform3D ComputePivot(bool useLocal)
    {
        IReadOnlyList<SceneEntity> selection = Movable;
        if (selection.Count == 0)
        {
            return Transform3D.Identity;
        }


        GVector3 centre = GVector3.Zero;
        foreach (SceneEntity obj in selection)
        {
            centre += obj.Transform * obj.LocalBounds.GetCenter();
        }

        centre /= selection.Count;

        Basis basis = useLocal ? selection[0].Transform.Basis.Orthonormalized() : Basis.Identity;
        return new Transform3D(basis, centre);
    }

    public static bool WorldToScreen(Camera3D camera, GVector3 world, NVector2 imageMin, out NVector2 screen)
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

    private static void DrawMarquee(NVector2 a, NVector2 b)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        NVector2 min = NVector2.Min(a, b);
        NVector2 max = NVector2.Max(a, b);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new NVector4(0.30f, 0.55f, 0.95f, 0.20f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new NVector4(0.40f, 0.65f, 1.0f, 0.90f)));
    }

    /// <summary>
    /// Slab test: transform the ray into the box's local space and clip against its bounds.
    /// </summary>
    /// <param name="tHit">
    /// Distance to select this entity at when the box is all we have to go on — the entry distance,
    /// or the exit distance when the ray starts inside the box.
    /// </param>
    /// <param name="tNear">
    /// Lower bound on how close anything inside this box can be: the entry distance, clamped to zero
    /// when the ray starts inside. <see cref="Pick"/> sorts and early-outs on this, which is only
    /// sound because nothing in the box — triangle or <paramref name="tHit"/> — can be nearer than it.
    /// </param>
    private static bool TryRayBox(GVector3 origin, GVector3 dir, Transform3D boxTransform, Aabb bounds, out float tHit, out float tNear)
    {
        tHit = 0.0f;
        tNear = 0.0f;
        Transform3D inv = boxTransform.AffineInverse();
        GVector3 o = inv * origin;
        GVector3 d = inv.Basis * dir;
        GVector3 lo = bounds.Position;
        GVector3 hi = bounds.End;

        float[] oc = [o.X, o.Y, o.Z];
        float[] dc = [d.X, d.Y, d.Z];
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
        tNear = Mathf.Max(tMin, 0.0f);
        return true;
    }
}
