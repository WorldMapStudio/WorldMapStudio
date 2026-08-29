using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="ProceduralModel"/>s — the catalog of procedural meshes a
/// <see cref="ProceduralMeshComponent"/> references by id. Structured like
/// <see cref="MeshMaterialsWindow"/>: catalog edits go through the edit session, so they undo and
/// commit with everything else. Field editing itself is shared with the component inspector via
/// <see cref="ProceduralModelFieldEditor"/>.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ProceduralModelsWindow : Window
{
    public override string? Category => "Models";

    private const uint NameMaxLength = 128;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly ProceduralModelFieldEditor _fields;
    private readonly ProceduralModelPicker _picker;

    public ProceduralModelsWindow(WindowManager manager)
        : base("Procedural Models", startOpen: false, defaultSize: new Vector2(560.0f, 560.0f))
    {
        _context = manager.Context;
        ProceduralMeshSystem system = _context.ProceduralMeshes;
        _fields = new ProceduralModelFieldEditor(system, new MeshParameterEditor(new TextureAssetPicker(_context.Assets)));
        _picker = new ProceduralModelPicker(system, _context.Root);
    }

    private ProceduralMeshSystem Models => _context.ProceduralMeshes;

    protected override void DrawContent()
    {
        if (ImGui.Button("Add model"))
        {
            _picker.OpenCreate(_context.EditSessions, _context.Catalog, _ => { });
        }

        ImGui.Separator();

        foreach (ProceduralModel model in Models.Models.OrderBy(m => m.Name, System.StringComparer.OrdinalIgnoreCase).ToList())
        {
            ImGui.PushID(model.Id.Value.GetHashCode());
            int uses = Models.UsageCount(model.RecordId ?? -1);
            string header = uses > 0 ? $"{model.Name} ({uses} uses)##header" : $"{model.Name}##header";
            if (ImGui.CollapsingHeader(header))
            {
                ImGui.TextDisabled($"Id #{model.RecordId}");
                DrawName(model, model.Name, value => model.Name = value);

                _fields.Draw(_context.EditSessions, model);

                DrawFooter(model, uses);
            }

            ImGui.PopID();
        }

        _fields.DrawModals();
        _picker.Draw();
    }

    private void DrawName(ProceduralModel entity, string current, System.Action<string> set)
    {
        string value = current;
        if (ImGui.InputText("Name", ref value, NameMaxLength))
        {
            set(value);
        }

        _tracker.Track(_context.EditSessions, entity, "name", value, set);
    }

    private void DrawFooter(ProceduralModel model, int uses)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Duplicate(model);
        }

        ImGui.SameLine();
        if (uses > 0)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && uses == 0)
        {
            Delete(model);
        }

        if (uses > 0)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled($"in use by {uses} entities");
        }
    }

    private void Duplicate(ProceduralModel model)
    {
        var clone = new ProceduralModel
        {
            Name = UniqueName($"{model.Name} Copy", Models.Models.Select(m => m.Name)),
            FunctionId = model.FunctionId,
            Parameters = model.Parameters,
            FormatId = model.FormatId,
            Materials = model.Materials,
        };
        clone.ReplaceNetwork(model.Network);

        // Identified before it is added, so a reference created in the same session can target it.
        _context.Catalog.AssignId(clone);

        var command = new CreateCatalogEntityCommand(_context.Catalog, clone);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private void Delete(ProceduralModel model)
    {
        var command = new DeleteCatalogEntityCommand(_context.Catalog, model);
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
