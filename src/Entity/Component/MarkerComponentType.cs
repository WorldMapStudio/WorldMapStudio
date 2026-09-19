using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class MarkerComponentType : ISceneComponentType
{
    public MarkerComponentType(SceneComponentRegistry registry)
    {
    }

    public string TypeId => MarkerComponent.Kind;

    public string DisplayName => "Marker";

    public bool AttachesToBridgedEntities => true;

    public SceneComponent Create() => new MarkerComponent();

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var marker = (MarkerComponent)component;

        context.Fields.Field("Shape", () =>
        {
            int shape = (int)marker.Shape;
            if (ImGui.Combo("Shape", ref shape, "Plain\0Cube\0Sphere\0"))
            {
                ComponentFieldRecorder.Record(context, marker, "shape", marker.Shape, (MarkerShape)shape, value => marker.Shape = value);
            }
        });
    }
}
