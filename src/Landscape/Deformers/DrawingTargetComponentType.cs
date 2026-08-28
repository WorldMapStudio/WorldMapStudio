using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class DrawingTargetComponentType : ISceneComponentType
{
    private readonly LandscapeSystem _landscape;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 2.0f;

    public DrawingTargetComponentType(SceneComponentRegistry registry)
    {
        _landscape = registry.Context.Landscape;
    }

    public string TypeId => "drawing-target";

    public string DisplayName => "Drawing Target";

    public SceneComponent Create() => new DrawingTargetComponent();

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var target = (DrawingTargetComponent)component;

        float sizeX = target.WorldSizeX;
        if (ImGui.DragFloat("Width", ref sizeX, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeX = sizeX; }
        _tracker.Track(context.Sessions, target, "width", target.WorldSizeX, value => target.WorldSizeX = value);

        float sizeZ = target.WorldSizeZ;
        if (ImGui.DragFloat("Depth", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeZ = sizeZ; }
        _tracker.Track(context.Sessions, target, "depth", target.WorldSizeZ, value => target.WorldSizeZ = value);

        LandscapeDeformerInspector.DrawChannelCombo(context, _landscape.Catalog, target, target.Channel, value => target.Channel = value);

        float strength = target.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { target.Strength = strength; }
        _tracker.Track(context.Sessions, target, "strength", target.Strength, value => target.Strength = value);

        ImGui.TextDisabled($"{target.Width} x {target.Height} pixels");
        DrawResizeButton(context, target, 128);
        ImGui.SameLine();
        DrawResizeButton(context, target, 256);
        ImGui.SameLine();
        DrawResizeButton(context, target, 512);
        ImGui.SameLine();
        DrawClearButton(context, target);
    }

    private static void DrawResizeButton(InspectorContext context, DrawingTargetComponent target, int size)
    {
        bool current = target.Width == size && target.Height == size;
        if (current)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"{size}"))
        {
            int beforeWidth = target.Width;
            int beforeHeight = target.Height;
            byte[] beforePixels = target.CopyPixels();
            target.Resize(size, size);
            context.Sessions.Record(new ResizeDrawingTargetCommand(
                target,
                beforeWidth,
                beforeHeight,
                beforePixels,
                target.Width,
                target.Height,
                target.CopyPixels()));
        }

        if (current)
        {
            ImGui.EndDisabled();
        }
    }

    private static void DrawClearButton(InspectorContext context, DrawingTargetComponent target)
    {
        if (!ImGui.Button("Clear"))
        {
            return;
        }

        byte[] before = target.CopyPixels();
        target.ReplacePixels(new byte[target.Width * target.Height]);
        context.Sessions.Record(new SetDrawingTargetPixelsCommand(target, before, target.CopyPixels()));
    }
}
