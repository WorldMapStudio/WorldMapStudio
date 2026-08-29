using System.Linq;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class RoadComponentType : ISceneComponentType
{
    private readonly LandscapeSystem _landscape;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 4.0f;

    public RoadComponentType(SceneComponentRegistry registry)
    {
        _landscape = registry.Context.Landscape;
    }

    public string TypeId => "landscape-road";

    public string DisplayName => "Road";

    public SceneComponent Create()
    {
        var road = new RoadComponent();

        LandscapeCatalog catalog = _landscape.Catalog;
        road.CentreChannel = catalog.Channels.FirstOrDefault()?.Name ?? "";
        road.ShoulderChannel = catalog.Channels.Skip(1).FirstOrDefault()?.Name ?? road.CentreChannel;

        var network = new VertexNetwork();
        int a = network.AddVertex(new Vector3(-5.0f, 0.0f, 0.0f));
        int b = network.AddVertex(new Vector3(5.0f, 0.0f, 0.0f));
        network.AddEdge(a, b);
        road.ReplaceNetwork(network);

        return road;
    }

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var road = (RoadComponent)component;
        LandscapeCatalog catalog = _landscape.Catalog;

        float centreWidth = road.CentreWidth;
        if (ImGui.DragFloat("Centre width", ref centreWidth, 0.1f, 0.0f, 256.0f)) { road.CentreWidth = centreWidth; }
        _tracker.Track(context.Sessions, road, "centre width", road.CentreWidth, value => road.CentreWidth = value);

        float shoulderWidth = road.ShoulderWidth;
        if (ImGui.DragFloat("Shoulder width", ref shoulderWidth, 0.1f, 0.0f, 256.0f)) { road.ShoulderWidth = shoulderWidth; }
        _tracker.Track(context.Sessions, road, "shoulder width", road.ShoulderWidth, value => road.ShoulderWidth = value);

        float falloff = road.Falloff;
        if (ImGui.DragFloat("Falloff", ref falloff, 0.01f, 0.0f, 1.0f)) { road.Falloff = falloff; }
        _tracker.Track(context.Sessions, road, "falloff", road.Falloff, value => road.Falloff = value);

        ImGui.Separator();
        ImGui.TextDisabled("Centre");
        ImGui.PushID("centre");
        LandscapeDeformerInspector.DrawChannelCombo(context, catalog, road, road.CentreChannel, value => road.CentreChannel = value);
        ImGui.PopID();

        ImGui.Separator();
        ImGui.TextDisabled("Shoulder");
        ImGui.PushID("shoulder");
        LandscapeDeformerInspector.DrawChannelCombo(context, catalog, road, road.ShoulderChannel, value => road.ShoulderChannel = value);
        ImGui.PopID();

        ImGui.Separator();
        ImGui.TextDisabled($"{road.Network.Vertices.Count} vertices, {road.Network.Edges.Count} edges, {road.Path.Segments.Count} spline segments");
    }
}
