using System.Linq;
using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class TerrainValueComponentType : ISceneComponentType
{
    private readonly LandscapeSystem _landscape;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 5.0f;

    public TerrainValueComponentType(SceneComponentRegistry registry)
    {
        _landscape = registry.Context.Landscape;
    }

    public string TypeId => TerrainValueComponent.Kind;

    public string DisplayName => "Terrain Value";

    public SceneComponent Create() => new TerrainValueComponent();

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var terrainValue = (TerrainValueComponent)component;

        float width = terrainValue.Width;
        if (ImGui.DragFloat("Width", ref width, 0.5f, 0.5f, 4096.0f)) { terrainValue.Width = width; }
        _tracker.Track(context.Sessions, terrainValue, "width", terrainValue.Width, value => terrainValue.Width = value);

        float height = terrainValue.Height;
        if (ImGui.DragFloat("Height", ref height, 0.5f, 0.5f, 4096.0f)) { terrainValue.Height = height; }
        _tracker.Track(context.Sessions, terrainValue, "height", terrainValue.Height, value => terrainValue.Height = value);

        float value = terrainValue.Value;
        if (ImGui.DragFloat("Value", ref value, 0.01f, -1.0f, 1.0f)) { terrainValue.Value = value; }
        _tracker.Track(context.Sessions, terrainValue, "value", terrainValue.Value, v => terrainValue.Value = v);

        LandscapeDeformerInspector.DrawChannelChecklist(
            context,
            _landscape.Catalog,
            terrainValue,
            terrainValue.Channels,
            terrainValue.ReplaceChannels);

        bool colorMatters = _landscape.Catalog.Channels
            .Any(c => terrainValue.Channels.Contains(c.Name) && c.Components > 1);
        if (colorMatters)
        {
            Godot.Color color = terrainValue.ColorValue;
            var colorEdit = new NVector4(color.R, color.G, color.B, color.A);
            if (ImGui.ColorEdit4("Color", ref colorEdit)) { terrainValue.ColorValue = new Godot.Color(colorEdit.X, colorEdit.Y, colorEdit.Z, colorEdit.W); }
            _tracker.Track(context.Sessions, terrainValue, "color", terrainValue.ColorValue, v => terrainValue.ColorValue = v);
        }
    }
}
