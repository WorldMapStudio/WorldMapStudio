using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ImageComponentType : ISceneComponentType
{
    private readonly ImageSystem _system;
    private readonly LandscapeSystem _landscape;
    private readonly ImagePicker _picker;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 2.0f;

    public ImageComponentType(SceneComponentRegistry registry)
    {
        _system = registry.Context.Images;
        _landscape = registry.Context.Landscape;
        _picker = new ImagePicker(_system);
    }

    public string TypeId => "image";

    public string DisplayName => "Image";

    public SceneComponent Create() => new ImageComponent(_system);

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var image = (ImageComponent)component;

        DrawImageReference(context, image);

        if (image.Image == null)
        {
            if (image.ImageId is { } danglingId)
            {
                ImGui.TextColored(new NVector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded image provides #{danglingId}.");
            }

            return;
        }

        ImGui.Separator();

        float sizeX = image.WorldSizeX;
        if (ImGui.DragFloat("Width", ref sizeX, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeX = sizeX; }
        _tracker.Track(context.Sessions, image, "width", image.WorldSizeX, value => image.WorldSizeX = value);

        float sizeZ = image.WorldSizeZ;
        if (ImGui.DragFloat("Depth", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeZ = sizeZ; }
        _tracker.Track(context.Sessions, image, "depth", image.WorldSizeZ, value => image.WorldSizeZ = value);

        LandscapeDeformerInspector.DrawChannelCombo(context, _landscape.Catalog, image, image.Channel, value => image.Channel = value);

        float strength = image.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { image.Strength = strength; }
        _tracker.Track(context.Sessions, image, "strength", image.Strength, value => image.Strength = value);

        ImGui.TextDisabled($"{image.Image.Width} x {image.Image.Height} pixels");
        DrawResizeButton(context, image, 128);
        ImGui.SameLine();
        DrawResizeButton(context, image, 256);
        ImGui.SameLine();
        DrawResizeButton(context, image, 512);
        ImGui.SameLine();
        DrawClearButton(context, image);

        int uses = _system.UsageCount(image.Image.RecordId ?? -1);
        if (uses > 1)
        {
            ImGui.TextDisabled($"used by {uses} entities in this map");
        }
    }

    public void DrawModals() => _picker.Draw();

    private void DrawImageReference(InspectorContext context, ImageComponent image)
    {
        PaintImage? bound = image.Image;
        string label = bound != null
            ? $"{bound.Name} (#{bound.RecordId})"
            : image.ImageId == null ? "(none)" : $"#{image.ImageId} (missing)";

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Image:");
        ImGui.SameLine();
        ImGui.TextDisabled(label);

        if (ImGui.Button("Browse##image"))
        {
            _picker.Browse(image.ImageId, selected =>
                ComponentFieldRecorder.Record(context, image, "image", image.ImageId, selected, value => image.ImageId = value));
        }

        ImGui.SameLine();
        if (ImGui.Button("New##image"))
        {
            _picker.OpenCreate(context.Sessions, _system.Context.Catalog, created =>
                ComponentFieldRecorder.Record(context, image, "image", image.ImageId, created.RecordId, value => image.ImageId = value));
        }

        if (image.ImageId != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##image"))
            {
                ComponentFieldRecorder.Record(context, image, "image", image.ImageId, null, value => image.ImageId = value);
            }
        }
    }

    private static void DrawResizeButton(InspectorContext context, ImageComponent component, int size)
    {
        PaintImage bound = component.Image!;
        bool current = bound.Width == size && bound.Height == size;
        if (current)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"{size}"))
        {
            int beforeWidth = bound.Width;
            int beforeHeight = bound.Height;
            byte[] beforePixels = bound.CopyPixels();
            bound.Resize(size, size);
            context.Sessions.Record(new SetImageContentCommand(
                bound,
                beforeWidth, beforeHeight, beforePixels,
                bound.Width, bound.Height, bound.CopyPixels(),
                component.AffectedEntities,
                $"Resize {bound.Name}"));
        }

        if (current)
        {
            ImGui.EndDisabled();
        }
    }

    private static void DrawClearButton(InspectorContext context, ImageComponent component)
    {
        if (!ImGui.Button("Clear"))
        {
            return;
        }

        PaintImage bound = component.Image!;
        byte[] before = bound.CopyPixels();
        bound.ReplacePixels(new byte[bound.Width * bound.Height]);
        context.Sessions.Record(new SetImageContentCommand(
            bound,
            bound.Width, bound.Height, before,
            bound.Width, bound.Height, bound.CopyPixels(),
            component.AffectedEntities,
            $"Clear {bound.Name}"));
    }
}
