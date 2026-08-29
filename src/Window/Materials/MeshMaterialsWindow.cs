using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="MeshMaterialPreset"/>s — the global catalog of saved materials a procedural
/// mesh's material slot binds to (procedural functions can also author a material inline; presets are
/// for values worth reusing and naming). Structured like <see cref="LandscapeMaterialsWindow"/>: catalog
/// edits go through the edit session, so they undo and commit with everything else.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class MeshMaterialsWindow : Window
{
    public override string? Category => "Materials";

    private const uint NameMaxLength = 128;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly MeshParameterEditor _parameterEditor;

    public MeshMaterialsWindow(WindowManager manager)
        : base("Mesh Materials", startOpen: false, defaultSize: new Vector2(560.0f, 520.0f))
    {
        _context = manager.Context;
        _parameterEditor = new MeshParameterEditor(new TextureAssetPicker(_context.Assets), _context.Landscape);
    }

    private MeshMaterialSystem Materials => _context.MeshMaterials;

    protected override void DrawContent()
    {
        if (ImGui.Button("Add material"))
        {
            Create(new MeshMaterialPreset { Name = UniqueName("Material", Materials.Presets.Select(m => m.Name)) });
        }

        ImGui.Separator();

        foreach (MeshMaterialIssue issue in Materials.Validate())
        {
            Vector4 color = issue.Severity == MeshMaterialIssueSeverity.Error
                ? new Vector4(1.0f, 0.45f, 0.4f, 1.0f)
                : new Vector4(1.0f, 0.72f, 0.22f, 1.0f);
            ImGui.TextColored(color, issue.Message);
        }

        foreach (MeshMaterialPreset preset in Materials.Presets.ToList())
        {
            ImGui.PushID(preset.Id.Value.GetHashCode());
            if (ImGui.CollapsingHeader($"{preset.Name}##header", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawName(preset, preset.Name, value => preset.Name = value);
                DrawType(preset);

                IMeshMaterialType? type = Materials.Find(preset.TypeId);
                if (type == null)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded type provides '{preset.TypeId}'.");
                }
                else
                {
                    ImGui.TextDisabled($"v{type.Version}");
                    MeshParameterValues values = MeshParameterValues.Parse(preset.Parameters);
                    _parameterEditor.Draw(type.Parameters, values, preset.Parameters, (before, after) =>
                        RecordNow(preset, "parameters", before, after, v => preset.Parameters = v));
                }

                DrawDelete(preset);
            }

            ImGui.PopID();
        }

        _parameterEditor.DrawModals();
    }

    private void DrawType(MeshMaterialPreset preset)
    {
        IMeshMaterialType? bound = Materials.Find(preset.TypeId);
        string label = bound?.DisplayName ?? (preset.TypeId.Length == 0 ? "(none)" : $"{preset.TypeId} (missing)");

        if (!ImGui.BeginCombo("Type", label))
        {
            return;
        }

        foreach (IMeshMaterialType type in Materials.All)
        {
            if (ImGui.Selectable($"{type.DisplayName}##{type.Id}", type.Id == preset.TypeId))
            {
                RecordNow(preset, "type", preset.TypeId, type.Id, v => preset.TypeId = v);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(type.Description);
            }
        }

        ImGui.EndCombo();
    }

    private void DrawName(MeshMaterialPreset entity, string current, System.Action<string> set)
    {
        string value = current;
        if (ImGui.InputText("Name", ref value, NameMaxLength))
        {
            set(value);
        }

        _tracker.Track(_context.EditSessions, entity, "name", value, set);
    }

    private void DrawDelete(MeshMaterialPreset preset)
    {
        ImGui.Spacing();
        if (!ImGui.SmallButton("Delete"))
        {
            return;
        }

        var command = new DeleteCatalogEntityCommand(_context.Catalog, preset);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private void Create(MeshMaterialPreset entity)
    {
        // Identified before it is added, so a slot binding created in the same session can reference it.
        _context.Catalog.AssignId(entity);

        var command = new CreateCatalogEntityCommand(_context.Catalog, entity);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    // A combo/parameter change has no activate/deactivate pair to bracket, so it is recorded on the spot.
    private void RecordNow<T>(CatalogEntity entity, string field, T before, T after, System.Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetFieldCommand<T>(entity, field, set, before, after);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private static string UniqueName(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix} {i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
