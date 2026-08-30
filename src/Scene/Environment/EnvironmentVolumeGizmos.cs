using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Draws a translucent inner/outer sphere pair for every loaded, in-view <see cref="IEnvironmentVolume"/>
/// component — the generic counterpart of Noggit's light-sphere visualisation. Owned and updated once
/// per frame by <see cref="ViewportWindow"/>, gated on <see cref="ViewSettings.ShowEnvironmentVolumes"/>.
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
    private readonly Dictionary<SceneComponent, VolumeGizmo> _gizmos = new();

    public EnvironmentVolumeGizmos(Node viewport, SceneEntityRegistry scene, ViewSettings view)
    {
        _root = viewport;
        _scene = scene;
        _view = view;
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

    // Unit sphere mesh, scaled per frame, so no geometry needs rebuilding as a radius is dragged.
    private static void ApplySphere(MeshInstance3D node, Vector3 origin, float radius, Color color)
    {
        node.Visible = radius > 0.001f;
        node.GlobalPosition = origin;
        node.Scale = Vector3.One * radius;
        ((StandardMaterial3D)node.MaterialOverride).AlbedoColor = new Color(color.R, color.G, color.B, 0.12f);
    }

    private static MeshInstance3D BuildSphere(string name) => new()
    {
        Name = name,
        Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 24, Rings = 12 },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        },
    };

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
