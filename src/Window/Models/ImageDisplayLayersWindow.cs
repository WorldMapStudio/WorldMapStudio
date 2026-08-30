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
    private readonly ImageDisplayLayerPicker _picker = new();

    public ImageDisplayLayersWindow(WindowManager manager)
        : base("Image Display Layers", startOpen: false, defaultSize: new Vector2(420.0f, 480.0f))
    {
        _context = manager.Context;
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
                    DrawOverlayColor(layer);
                }

                DrawFooter(layer, uses);
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

    private void DrawOverlayColor(ImageDisplayLayer layer)
    {
        Godot.Color color = layer.OverlayColor;
        var value = new Vector4(color.R, color.G, color.B, color.A);
        if (ImGui.ColorEdit4("Overlay Color", ref value))
        {
            layer.OverlayColor = new Godot.Color(value.X, value.Y, value.Z, value.W);
        }

        _tracker.Track(_context.EditSessions, layer, "overlay color", layer.OverlayColor, c => layer.OverlayColor = c);
    }

    private void DrawFooter(ImageDisplayLayer layer, int uses)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Duplicate(layer);
        }

        ImGui.SameLine();
        if (uses > 0)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && uses == 0)
        {
            Delete(layer);
        }

        if (uses > 0)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled($"in use by {uses} entities");
        }
    }

    private void Duplicate(ImageDisplayLayer layer)
    {
        var clone = new ImageDisplayLayer
        {
            Name = UniqueName($"{layer.Name} Copy", Images.DisplayLayers.Select(l => l.Name)),
            DisplayMode = layer.DisplayMode,
            OverlayColor = layer.OverlayColor,
        };

        // Identified before it is added, so a reference created in the same session can target it.
        _context.Catalog.AssignId(clone);

        var command = new CreateCatalogEntityCommand(_context.Catalog, clone);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private void Delete(ImageDisplayLayer layer)
    {
        var command = new DeleteCatalogEntityCommand(_context.Catalog, layer);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private static string UniqueName(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        if (used.Add(prefix))
        {
            return prefix;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{prefix} {i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
