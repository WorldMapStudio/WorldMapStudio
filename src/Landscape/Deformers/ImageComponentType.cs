using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class ImageComponentType : ISceneComponentType
{
    private readonly ImageSystem _system;
    private readonly LandscapeSystem _landscape;
    private readonly ImagePicker _picker;
    private readonly ImageDisplayLayerPicker _layerPicker = new();
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
        DrawDisplayLayerCombo(context, image);

        if (image.Image == null)
        {
            if (image.ImageId is { } danglingId)
            {
                ImGui.TextColored(new NVector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded image provides #{danglingId}.");
            }

            return;
        }

        ImGui.Separator();

        // "World" makes clear this is the placement's footprint in world units, not the image's own
        // pixel resolution shown below — the two used to share the plain "Width" label and that read
        // as one setting instead of two unrelated ones.
        float sizeX = image.WorldSizeX;
        if (ImGui.DragFloat("World Size X", ref sizeX, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeX = sizeX; }
        _tracker.Track(context.Sessions, image, "width", image.WorldSizeX, value => image.WorldSizeX = value);

        float sizeZ = image.WorldSizeZ;
        if (ImGui.DragFloat("World Size Z", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeZ = sizeZ; }
        _tracker.Track(context.Sessions, image, "depth", image.WorldSizeZ, value => image.WorldSizeZ = value);

        LandscapeDeformerInspector.DrawChannelCombo(context, _landscape.Catalog, image, image.Channel, value => image.Channel = value);

        float strength = image.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { image.Strength = strength; }
        _tracker.Track(context.Sessions, image, "strength", image.Strength, value => image.Strength = value);

        ImGui.Separator();
        ImGui.TextDisabled($"Canvas: {image.Image.Width} x {image.Image.Height} px, {image.Image.ChunkSize}px chunks (fixed at creation)");
        ImGui.TextDisabled(ChunkGridSummary(image.Image));
        DrawClearButton(context, image);

        int uses = _system.UsageCount(image.Image.RecordId ?? -1);
        if (uses > 1)
        {
            ImGui.TextDisabled($"used by {uses} entities in this map");
        }
    }

    public void DrawModals()
    {
        _picker.Draw();
        _layerPicker.Draw();
    }

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

    /// <summary>A plain combo rather than a full modal picker like <see cref="ImagePicker"/> — display
    /// layers are a small, hand-authored list (presets), not a large browsable catalog, the same
    /// weight of UI as <see cref="ProceduralModelFieldEditor.Draw"/>'s function combo.</summary>
    private void DrawDisplayLayerCombo(InspectorContext context, ImageComponent image)
    {
        ImageDisplayLayer? bound = image.DisplayLayer;
        string label = bound != null
            ? bound.Name
            : image.DisplayLayerId == null ? "(none)" : $"#{image.DisplayLayerId} (missing)";

        if (ImGui.BeginCombo("Display Layer", label))
        {
            if (ImGui.Selectable("(none)", image.DisplayLayerId == null))
            {
                ComponentFieldRecorder.Record(context, image, "display layer", image.DisplayLayerId, (int?)null, value => image.DisplayLayerId = value);
            }

            foreach (ImageDisplayLayer layer in _system.DisplayLayers.OrderBy(l => l.Name, System.StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"{layer.Name}##{layer.RecordId}", layer.RecordId == image.DisplayLayerId))
                {
                    int? selected = layer.RecordId;
                    ComponentFieldRecorder.Record(context, image, "display layer", image.DisplayLayerId, selected, value => image.DisplayLayerId = value);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("New##displaylayer"))
        {
            _layerPicker.OpenCreate(context.Sessions, _system.Context.Catalog, created =>
                ComponentFieldRecorder.Record(context, image, "display layer", image.DisplayLayerId, created.RecordId, value => image.DisplayLayerId = value));
        }
    }

    private static string ChunkGridSummary(PaintImage image)
    {
        double residentMb = image.ResidentByteSize / (1024.0 * 1024.0);
        return $"{image.ChunksX}x{image.ChunksY} chunk grid, {image.ChunkCount} resident ({residentMb:F1} MB)";
    }

    /// <summary>Drops every resident chunk via <see cref="PaintImage.ClearAll"/> and records it through
    /// <see cref="PaintImageChunksCommand"/> — every cleared chunk going to null is exactly the shape
    /// that command already understands, so no separate "clear" command type is needed.</summary>
    private static void DrawClearButton(InspectorContext context, ImageComponent component)
    {
        if (!ImGui.Button("Clear"))
        {
            return;
        }

        PaintImage bound = component.Image!;
        IReadOnlyList<(ImageChunkCoord Coord, byte[] Pixels)> cleared = bound.ClearAll();
        if (cleared.Count == 0)
        {
            return;
        }

        var edits = cleared.Select(entry => (entry.Coord, (byte[]?)entry.Pixels, (byte[]?)null)).ToList();
        context.Sessions.Record(new PaintImageChunksCommand(bound, edits, component.AffectedEntities, $"Clear {bound.Name}"));
    }
}
