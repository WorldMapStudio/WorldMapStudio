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
                ImGui.TextDisabled($"Id #{image.RecordId} · {image.Width}x{image.Height}");
                DrawName(image, image.Name, value => image.Name = value);
                DrawFooter(image, uses);
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

    private void DrawFooter(PaintImage image, int uses)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Duplicate(image);
        }

        ImGui.SameLine();
        if (uses > 0)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && uses == 0)
        {
            Delete(image);
        }

        if (uses > 0)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled($"in use by {uses} entities");
        }
    }

    private void Duplicate(PaintImage image)
    {
        var clone = new PaintImage { Name = UniqueName($"{image.Name} Copy", Images.Images.Select(m => m.Name)) };
        clone.LoadPixels(image.Width, image.Height, image.CopyPixels());

        // Identified before it is added, so a reference created in the same session can target it.
        _context.Catalog.AssignId(clone);

        var command = new CreateCatalogEntityCommand(_context.Catalog, clone);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private void Delete(PaintImage image)
    {
        var command = new DeleteCatalogEntityCommand(_context.Catalog, image);
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
