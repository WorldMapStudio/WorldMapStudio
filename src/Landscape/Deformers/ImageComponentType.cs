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
    private readonly ImageDisplayLayerPicker _layerPicker;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 2.0f;

    public ImageComponentType(SceneComponentRegistry registry)
    {
        _system = registry.Context.Images;
        _landscape = registry.Context.Landscape;
        _picker = new ImagePicker(_system);
        _layerPicker = new ImageDisplayLayerPicker(_system);
    }

    public string TypeId => ImageComponent.Kind;

    public string DisplayName => "Image";

    public SceneComponent Create() => new ImageComponent(_system);

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var image = (ImageComponent)component;
        FieldFilter fields = context.Fields;

        fields.Field("Image", () => DrawImageReference(context, image));
        fields.Field("Display Layer", () => DrawDisplayLayerCombo(context, image));

        if (image.Image == null)
        {
            if (image.ImageId is { } danglingId)
            {
                fields.Chrome(() =>
                    ImGui.TextColored(new NVector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded image provides #{danglingId}."));
            }

            return;
        }

        fields.Separator();

        // "Footprint" for how large the projection is on the ground, distinct from both the entity's
        // own transform (position/rotation, drawn elsewhere and untouched by this component) and the
        // image's pixel resolution (shown below) — three different "size"-shaped settings that must
        // not share names.
        fields.Field("Footprint Width", () =>
        {
            float sizeX = image.WorldSizeX;
            if (ImGui.DragFloat("Footprint Width", ref sizeX, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeX = sizeX; }
            _tracker.Track(context.Sessions, image, "width", image.WorldSizeX, value => image.WorldSizeX = value);
        });

        fields.Field("Footprint Depth", () =>
        {
            float sizeZ = image.WorldSizeZ;
            if (ImGui.DragFloat("Footprint Depth", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { image.WorldSizeZ = sizeZ; }
            _tracker.Track(context.Sessions, image, "depth", image.WorldSizeZ, value => image.WorldSizeZ = value);
        });

        fields.Field("Channel", () => DrawChannelBinding(context, image));

        fields.Field("Strength", () =>
        {
            float strength = image.Strength;
            if (ImGui.DragFloat("Strength", ref strength, 0.5f, 0.0f, float.MaxValue)) { image.Strength = strength; }
            _tracker.Track(context.Sessions, image, "strength", image.Strength, value => image.Strength = value);
        });

        fields.Field("Write Mode", () => DrawWriteModeCombo(context, image));

        fields.Separator();
        fields.Chrome(() =>
        {
            ImGui.TextDisabled(ChunkGridSummary(image.Image));
            ImGui.TextDisabled(ResidencySummary(image.Image));
        });
        fields.Field("Clear", () => DrawClearButton(context, image));

        int uses = _system.UsageCount(image.Image.RecordId ?? -1);
        if (uses > 1)
        {
            fields.Chrome(() => ImGui.TextDisabled($"used by {uses} entities in this map"));
        }
    }

    public void DrawModals()
    {
        _picker.Draw();
        _layerPicker.Draw();
    }

    private static readonly LandscapeSwizzle[] SwizzleChoices =
    [
        LandscapeSwizzle.Native, LandscapeSwizzle.R, LandscapeSwizzle.G, LandscapeSwizzle.B, LandscapeSwizzle.A,
        LandscapeSwizzle.Rgb, LandscapeSwizzle.Rgba, LandscapeSwizzle.Luminance,
    ];

    private static string SwizzleLabel(LandscapeSwizzle swizzle) => swizzle switch
    {
        LandscapeSwizzle.Native => "Native",
        LandscapeSwizzle.R => "R",
        LandscapeSwizzle.G => "G",
        LandscapeSwizzle.B => "B",
        LandscapeSwizzle.A => "A",
        LandscapeSwizzle.Rgb => "RGB",
        LandscapeSwizzle.Rgba => "RGBA",
        LandscapeSwizzle.Luminance => "Luminance",
        _ => swizzle.ToString(),
    };

    /// <summary>
    /// Which channel this placement writes, plus — only when the bound image itself carries color —
    /// which of the image's own components to take: <see cref="ImageComponent.Rasterize"/> parses
    /// <see cref="ImageComponent.Channel"/> as a <see cref="LandscapeChannelBinding"/> and applies the
    /// swizzle to what it <em>samples from the image</em>, not to the destination channel — a scalar
    /// image has only ever one number to give regardless of which swizzle is picked, so the picker is
    /// hidden rather than offering a choice that cannot change anything.
    /// </summary>
    private void DrawChannelBinding(InspectorContext context, ImageComponent image)
    {
        LandscapeChannelBinding binding = LandscapeChannelBinding.Parse(image.Channel);
        string label = binding.IsEmpty ? "(none)" : binding.Channel;

        if (ImGui.BeginCombo("Channel", label))
        {
            if (ImGui.Selectable("(none)", binding.IsEmpty))
            {
                ComponentFieldRecorder.Record(context, image, "channel", image.Channel, "", value => image.Channel = value);
            }

            foreach (LandscapeChannel item in _landscape.Catalog.Channels)
            {
                if (ImGui.Selectable(item.Name, item.Name == binding.Channel))
                {
                    var next = new LandscapeChannelBinding(item.Name, binding.Swizzle);
                    ComponentFieldRecorder.Record(context, image, "channel", image.Channel, next.ToString(), value => image.Channel = value);
                }
            }

            ImGui.EndCombo();
        }

        if (binding.IsEmpty || (image.Image?.Components ?? 1) == 1)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.BeginCombo("Image component", SwizzleLabel(binding.Swizzle)))
        {
            foreach (LandscapeSwizzle candidate in SwizzleChoices)
            {
                if (ImGui.Selectable(SwizzleLabel(candidate), candidate == binding.Swizzle))
                {
                    var next = new LandscapeChannelBinding(binding.Channel, candidate);
                    ComponentFieldRecorder.Record(context, image, "channel component", image.Channel, next.ToString(), value => image.Channel = value);
                }
            }

            ImGui.EndCombo();
        }
    }

    private static readonly ImageWriteMode[] WriteModeChoices = [ImageWriteMode.Max, ImageWriteMode.Replace];

    private static string WriteModeLabel(ImageWriteMode mode) => mode switch
    {
        ImageWriteMode.Max => "Max (combine)",
        ImageWriteMode.Replace => "Replace (overwrite)",
        _ => mode.ToString(),
    };

    /// <summary>How a scalar write lands on its channel — see <see cref="ImageWriteMode"/>. Has no
    /// effect on a color destination, which always combines by max.</summary>
    private static void DrawWriteModeCombo(InspectorContext context, ImageComponent image)
    {
        if (!ImGui.BeginCombo("Write Mode", WriteModeLabel(image.WriteMode)))
        {
            return;
        }

        foreach (ImageWriteMode mode in WriteModeChoices)
        {
            if (ImGui.Selectable(WriteModeLabel(mode), mode == image.WriteMode))
            {
                ComponentFieldRecorder.Record(context, image, "write mode", image.WriteMode, mode, value => image.WriteMode = value);
            }
        }

        ImGui.EndCombo();
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

    private static string ChunkGridSummary(PaintImage image) =>
        $"{image.ChunkSize}px chunks, {image.ChunksX}x{image.ChunksY} grid = {image.Width}x{image.Height}px total (fixed at creation)";

    private static string ResidencySummary(PaintImage image)
    {
        double residentMb = image.ResidentByteSize / (1024.0 * 1024.0);
        return $"{image.ChunkCount} resident ({residentMb:F1} MB)";
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
