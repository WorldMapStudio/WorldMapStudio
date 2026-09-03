using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Draws a wireframe inner/outer sphere pair for the selected entity carrying an
/// <see cref="IEnvironmentVolume"/> component, purely as a placement/sizing aid — it has no bearing
/// on the actual environment blend (sky, fog, ambient), which <see cref="EnvironmentSystem"/> and
/// <see cref="EnvironmentRenderer"/> compute and apply every frame for every loaded source regardless
/// of selection. Line geometry rather than a filled mesh so it never washes the screen out the way a
/// solid translucent sphere does when the camera sits inside it. Owned and updated once per frame by
/// <see cref="ViewportWindow"/>, gated on <see cref="ViewSettings.ShowEnvironmentVolumes"/>.
/// </summary>
public sealed class EnvironmentVolumeGizmos
{
    private sealed class VolumeGizmo
    {
        public required MeshInstance3D Inner;
        public required MeshInstance3D Outer;
    }

    private readonly Node _root;
    private readonly SceneEntityRegistry _scene;
    private readonly ViewSettings _view;
    private readonly SelectionSystem _selection;
    private readonly Dictionary<SceneComponent, VolumeGizmo> _gizmos = new();

    public EnvironmentVolumeGizmos(Node viewport, SceneEntityRegistry scene, ViewSettings view, SelectionSystem selection)
    {
        _root = viewport;
        _scene = scene;
        _view = view;
        _selection = selection;
    }

    public void Update()
    {
        if (!_view.ShowEnvironmentVolumes)
        {
            ClearAll();
            return;
        }

        var seen = new HashSet<SceneComponent>();
        foreach (SceneEntity entity in _scene.InView)
        {
            if (!_selection.IsSelected(entity))
            {
                continue;
            }

            Vector3 origin = entity.Transform.Origin;
            foreach (SceneComponent component in entity.Components)
            {
                if (component is not IEnvironmentVolume volume)
                {
                    continue;
                }

                seen.Add(component);
                Sync(GetOrCreate(component), origin, volume);
            }
        }

        foreach (SceneComponent stale in _gizmos.Keys.Where(component => !seen.Contains(component)).ToList())
        {
            Remove(stale);
        }
    }

    private VolumeGizmo GetOrCreate(SceneComponent component)
    {
        if (_gizmos.TryGetValue(component, out VolumeGizmo? existing))
        {
            return existing;
        }

        var gizmo = new VolumeGizmo
        {
            Inner = BuildSphere("EnvironmentVolumeInner"),
            Outer = BuildSphere("EnvironmentVolumeOuter"),
        };
        _root.AddChild(gizmo.Inner);
        _root.AddChild(gizmo.Outer);
        _gizmos[component] = gizmo;
        return gizmo;
    }

    private static void Sync(VolumeGizmo gizmo, Vector3 origin, IEnvironmentVolume volume)
    {
        ApplySphere(gizmo.Inner, origin, volume.InnerRadius, volume.InnerColor);
        ApplySphere(gizmo.Outer, origin, volume.OuterRadius, volume.OuterColor);
    }

    // Unit-radius wireframe mesh, scaled per frame, so no geometry needs rebuilding as a radius is
    // dragged.
    private static void ApplySphere(MeshInstance3D node, Vector3 origin, float radius, Color color)
    {
        node.Visible = radius > 0.001f;
        node.GlobalPosition = origin;
        node.Scale = Vector3.One * radius;
        ((StandardMaterial3D)node.MaterialOverride).AlbedoColor = new Color(color.R, color.G, color.B, 0.85f);
    }

    private static MeshInstance3D BuildSphere(string name) => new()
    {
        Name = name,
        Mesh = BuildWireSphereMesh(),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        },
    };

    // Three orthogonal great circles rather than a filled SphereMesh: cheap, unmistakably a debug
    // aid rather than scene geometry, and — since lines have no interior — never fills the screen
    // when the camera ends up inside the radius.
    private const int WireSegments = 48;

    private static Mesh BuildWireSphereMesh()
    {
        var vertices = new List<Vector3>();
        AddCircleLines(vertices, Vector3.Right, Vector3.Up);
        AddCircleLines(vertices, Vector3.Up, Vector3.Back);
        AddCircleLines(vertices, Vector3.Right, Vector3.Back);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    private static void AddCircleLines(List<Vector3> vertices, Vector3 u, Vector3 v)
    {
        for (int i = 0; i < WireSegments; i++)
        {
            float t0 = i / (float)WireSegments * Mathf.Tau;
            float t1 = (i + 1) / (float)WireSegments * Mathf.Tau;
            vertices.Add((u * Mathf.Cos(t0)) + (v * Mathf.Sin(t0)));
            vertices.Add((u * Mathf.Cos(t1)) + (v * Mathf.Sin(t1)));
        }
    }

    /// <summary>Frees every gizmo node. Called both when the toggle turns off and across a world
    /// reload, since nothing else notices a component's owning entity vanished underneath it.</summary>
    public void Unload() => ClearAll();

    private void ClearAll()
    {
        foreach (SceneComponent component in _gizmos.Keys.ToList())
        {
            Remove(component);
        }
    }

    private void Remove(SceneComponent component)
    {
        VolumeGizmo gizmo = _gizmos[component];
        gizmo.Inner.QueueFree();
        gizmo.Outer.QueueFree();
        _gizmos.Remove(component);
    }
}
