using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="PaintImage"/>s — the catalog of painted rasters an <see cref="ImageComponent"/>
/// references by id. Structured like <see cref="ProceduralModelsWindow"/>: catalog edits go through
/// the edit session, so they undo and commit with everything else.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ImagesWindow : Window
{
    public override string? Category => "Models";

    private const uint NameMaxLength = 128;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly ImagePicker _picker;

    public ImagesWindow(WindowManager manager)
        : base("Images", startOpen: false, defaultSize: new Vector2(420.0f, 480.0f))
    {
        _context = manager.Context;
        _picker = new ImagePicker(_context.Images);
    }

    private ImageSystem Images => _context.Images;

    protected override void DrawContent()
    {
        if (ImGui.Button("Add image"))
        {
            _picker.OpenCreate(_context.EditSessions, _context.Catalog, _ => { });
        }

        ImGui.Separator();

        foreach (PaintImage image in Images.Images.OrderBy(m => m.Name, System.StringComparer.OrdinalIgnoreCase).ToList())
        {
            ImGui.PushID(image.Id.Value.GetHashCode());
            int uses = Images.UsageCount(image.RecordId ?? -1);
            string header = uses > 0 ? $"{image.Name} ({uses} uses)##header" : $"{image.Name}##header";
            if (ImGui.CollapsingHeader(header))
            {
                string formatSuffix = image.Format == PaintImagePixelFormat.Float32 ? " (f32)" : "";
                ImGui.TextDisabled($"Id #{image.RecordId} · {image.Width}x{image.Height} · {ComponentsLabel(image.Components)}{formatSuffix}");
                if (image.IsDiskBacked)
                {
                    ImGui.TextDisabled($"Disk: {image.DiskPath}{(image.IsTiledDisk ? $"  ({image.DiskTilePattern})" : "")}");
                }

                DrawName(image, image.Name, value => image.Name = value);
                DrawFooter(image);
            }

            ImGui.PopID();
        }

        _picker.Draw();
    }

    private void DrawName(PaintImage entity, string current, System.Action<string> set)
    {
        string value = current;
        if (ImGui.InputText("Name", ref value, NameMaxLength))
        {
            set(value);
        }

        _tracker.Track(_context.EditSessions, entity, "name", value, set);
    }

    private void DrawFooter(PaintImage image)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Apply(Images.BuildDuplicateCommand(image, out _));
        }

        ImGui.SameLine();
        string? blocker = Images.DeleteBlocker(image);
        if (blocker != null)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && blocker == null)
        {
            Apply(Images.BuildDeleteCommand(image));
        }

        if (blocker != null)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(blocker);
        }
    }

    private static string ComponentsLabel(int components) => components switch
    {
        3 => "RGB",
        4 => "RGBA",
        _ => "Scalar",
    };

    private void Apply(IEditCommand command)
    {
        command.Apply();
        _context.EditSessions.Record(command);
    }
}
