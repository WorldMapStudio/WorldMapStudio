using ImGuiNET;

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

    public string TypeId => "landscape-terrain-value";

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
    }
}
