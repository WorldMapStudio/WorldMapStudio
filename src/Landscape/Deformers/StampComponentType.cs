using System.Linq;
using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class StampComponentType : ISceneComponentType
{
    private readonly LandscapeSystem _landscape;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 1.0f;

    public StampComponentType(SceneComponentRegistry registry)
    {
        _landscape = registry.Context.Landscape;
    }

    public string TypeId => "landscape-stamp";

    public string DisplayName => "Landscape Stamp";

    public SceneComponent Create() => new StampComponent();

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var stamp = (StampComponent)component;

        float radius = stamp.Radius;
        if (ImGui.DragFloat("Radius", ref radius, 0.5f, 0.5f, 4096.0f)) { stamp.Radius = radius; }
        _tracker.Track(context.Sessions, stamp, "radius", stamp.Radius, value => stamp.Radius = value);

        float falloff = stamp.Falloff;
        if (ImGui.DragFloat("Falloff", ref falloff, 0.01f, 0.0f, 1.0f)) { stamp.Falloff = falloff; }
        _tracker.Track(context.Sessions, stamp, "falloff", stamp.Falloff, value => stamp.Falloff = value);

        LandscapeDeformerInspector.DrawChannelCombo(context, _landscape.Catalog, stamp, stamp.Channel, value => stamp.Channel = value);

        float strength = stamp.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { stamp.Strength = strength; }
        _tracker.Track(context.Sessions, stamp, "strength", stamp.Strength, value => stamp.Strength = value);

        bool colorMatters = _landscape.Catalog.Channels.FirstOrDefault(c => c.Name == stamp.Channel)?.Components > 1;
        if (colorMatters)
        {
            Godot.Color color = stamp.ColorValue;
            var value = new NVector4(color.R, color.G, color.B, color.A);
            if (ImGui.ColorEdit4("Color", ref value)) { stamp.ColorValue = new Godot.Color(value.X, value.Y, value.Z, value.W); }
            _tracker.Track(context.Sessions, stamp, "color", stamp.ColorValue, value2 => stamp.ColorValue = value2);
        }
    }
}
