using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="ImageDisplayLayer"/>s — the catalog of preview presets an
/// <see cref="ImageComponent"/> references by id. Structured like <see cref="ImagesWindow"/>: catalog
/// edits go through the edit session, so they undo and commit with everything else.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ImageDisplayLayersWindow : Window
{
    public override string? Category => "Models";

    private const uint NameMaxLength = 128;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly ImageDisplayLayerPicker _picker;

    public ImageDisplayLayersWindow(WindowManager manager)
        : base("Image Display Layers", startOpen: false, defaultSize: new Vector2(420.0f, 480.0f))
    {
        _context = manager.Context;
        _picker = new ImageDisplayLayerPicker(_context.Images);
    }

    private ImageSystem Images => _context.Images;

    protected override void DrawContent()
    {
        if (ImGui.Button("Add layer"))
        {
            _picker.OpenCreate(_context.EditSessions, _context.Catalog, _ => { });
        }

        ImGui.Separator();

        foreach (ImageDisplayLayer layer in Images.DisplayLayers.OrderBy(l => l.Name, System.StringComparer.OrdinalIgnoreCase).ToList())
        {
            ImGui.PushID(layer.Id.Value.GetHashCode());
            int uses = Images.DisplayLayerUsageCount(layer.RecordId ?? -1);
            string header = uses > 0 ? $"{layer.Name} ({uses} uses)##header" : $"{layer.Name}##header";
            if (ImGui.CollapsingHeader(header))
            {
                ImGui.TextDisabled($"Id #{layer.RecordId}");
                DrawName(layer, layer.Name, value => layer.Name = value);
                DrawDisplayMode(layer);

                if (layer.DisplayMode == ImageDisplayMode.LandscapeOverlay)
                {
                    DrawColorSource(layer);

                    if (layer.ColorSource == ImageColorSource.Ramp)
                    {
                        DrawColor("Base Color (value 0)", layer, () => layer.BaseColor, c => layer.BaseColor = c);
                        DrawColor("Full Color (value 255)", layer, () => layer.FullColor, c => layer.FullColor = c);
                    }
                }

                DrawFooter(layer);
            }

            ImGui.PopID();
        }

        _picker.Draw();
    }

    private void DrawName(ImageDisplayLayer layer, string current, System.Action<string> set)
    {
        string value = current;
        if (ImGui.InputText("Name", ref value, NameMaxLength))
        {
            set(value);
        }

        _tracker.Track(_context.EditSessions, layer, "name", value, set);
    }

    private void DrawDisplayMode(ImageDisplayLayer layer)
    {
        string label = layer.DisplayMode switch
        {
            ImageDisplayMode.LandscapeOverlay => "Landscape Overlay",
            ImageDisplayMode.Object => "Object",
            _ => "None",
        };

        if (!ImGui.BeginCombo("Display Mode", label))
        {
            return;
        }

        foreach (ImageDisplayMode mode in new[] { ImageDisplayMode.None, ImageDisplayMode.LandscapeOverlay, ImageDisplayMode.Object })
        {
            string modeLabel = mode switch
            {
                ImageDisplayMode.LandscapeOverlay => "Landscape Overlay",
                ImageDisplayMode.Object => "Object",
                _ => "None",
            };

            if (ImGui.Selectable(modeLabel, mode == layer.DisplayMode) && layer.DisplayMode != mode)
            {
                var command = new SetFieldCommand<ImageDisplayMode>(layer, "display mode", value => layer.DisplayMode = value, layer.DisplayMode, mode);
                command.Apply();
                _context.EditSessions.Record(command);
            }
        }

        ImGui.EndCombo();
    }

    private void DrawColorSource(ImageDisplayLayer layer)
    {
        string label = layer.ColorSource == ImageColorSource.Direct ? "Direct" : "Ramp";
        if (!ImGui.BeginCombo("Color Source", label))
        {
            return;
        }

        foreach (ImageColorSource source in new[] { ImageColorSource.Ramp, ImageColorSource.Direct })
        {
            string sourceLabel = source == ImageColorSource.Direct ? "Direct" : "Ramp";
            if (ImGui.Selectable(sourceLabel, source == layer.ColorSource) && layer.ColorSource != source)
            {
                var command = new SetFieldCommand<ImageColorSource>(layer, "color source", value => layer.ColorSource = value, layer.ColorSource, source);
                command.Apply();
                _context.EditSessions.Record(command);
            }
        }

        ImGui.EndCombo();
        ImGui.SameLine();
        ImGui.TextDisabled(layer.ColorSource == ImageColorSource.Direct ? "(the image's own RGB(A))" : "(ramps a single value)");
    }

    private void DrawColor(string label, ImageDisplayLayer layer, System.Func<Godot.Color> get, System.Action<Godot.Color> set)
    {
        Godot.Color color = get();
        var value = new Vector4(color.R, color.G, color.B, color.A);
        if (ImGui.ColorEdit4(label, ref value))
        {
            set(new Godot.Color(value.X, value.Y, value.Z, value.W));
        }

        _tracker.Track(_context.EditSessions, layer, label, get(), set);
    }

    private void DrawFooter(ImageDisplayLayer layer)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Apply(Images.BuildDuplicateLayerCommand(layer, out _));
        }

        ImGui.SameLine();
        string? blocker = Images.DeleteBlocker(layer);
        if (blocker != null)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && blocker == null)
        {
            Apply(Images.BuildDeleteLayerCommand(layer));
        }

        if (blocker != null)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(blocker);
        }
    }

    private void Apply(IEditCommand command)
    {
        command.Apply();
        _context.EditSessions.Record(command);
    }
}
