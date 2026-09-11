using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="ProceduralModel"/>s — the catalog of procedural meshes a
/// <see cref="ProceduralComponent"/> references by id. Structured like
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

    // Stored placements — not loaded ones — are what makes Delete safe: a model whose only placements
    // are streamed out has zero loaded uses but still has rows referencing it in storage. Queried once
    // per model when its row is expanded, not every frame, and invalidated whenever the catalog or
    // scene changes (a create, delete, or commit can all move this count).
    private readonly Dictionary<int, int> _storedPlacementCounts = [];
    private (int Catalog, int Scene) _storedPlacementCountsFor = (-1, -1);

    public ProceduralModelsWindow(WindowManager manager)
        : base("Procedural Models", startOpen: false, defaultSize: new Vector2(560.0f, 560.0f))
    {
        _context = manager.Context;
        ProceduralSystem system = _context.Procedural;
        _fields = new ProceduralModelFieldEditor(system, new MeshParameterEditor(new TextureAssetPicker(_context.Assets), _context.Landscape));
        _picker = new ProceduralModelPicker(system, _context.Root);
    }

    private ProceduralSystem Models => _context.Procedural;

    protected override void DrawContent()
    {
        var versions = (_context.Catalog.Version, _context.Scene.Version);
        if (versions != _storedPlacementCountsFor)
        {
            _storedPlacementCountsFor = versions;
            _storedPlacementCounts.Clear();
        }

        if (ImGui.Button("Add model"))
        {
            _picker.OpenCreate(_context.EditSessions, _context.Catalog, _ => { });
        }

        ImGui.Separator();

        foreach (ProceduralModel model in Models.Models.OrderBy(m => m.Name, System.StringComparer.OrdinalIgnoreCase).ToList())
        {
            ImGui.PushID(model.Id.Value.GetHashCode());
            int loaded = Models.UsageCount(model.RecordId ?? -1);
            string header = loaded > 0 ? $"{model.Name} ({loaded} loaded placements)##header" : $"{model.Name}##header";
            if (ImGui.CollapsingHeader(header))
            {
                ImGui.TextDisabled($"Id #{model.RecordId}");
                DrawName(model, model.Name, value => model.Name = value);

                _fields.Draw(_context.EditSessions, model);

                DrawFooter(model, loaded, StoredPlacementCount(model));
            }

            ImGui.PopID();
        }

        _fields.DrawModals();
        _picker.Draw();
    }

    /// <summary>Every stored placement referencing this model, regardless of whether it is currently
    /// loaded — what actually makes deleting the model safe. Queried once per model per catalog/scene
    /// version and cached; see <see cref="_storedPlacementCounts"/>.</summary>
    private int StoredPlacementCount(ProceduralModel model)
    {
        if (model.RecordId is not int id)
        {
            return 0;
        }

        if (_storedPlacementCounts.TryGetValue(id, out int cached))
        {
            return cached;
        }

        int count = BlockingWork.Run(() => CountStoredPlacementsAsync(id));
        _storedPlacementCounts[id] = count;
        return count;
    }

    private async Task<int> CountStoredPlacementsAsync(int modelId)
    {
        EditorStorage storage = _context.Database.Storages.OfType<EditorStorage>().First();
        var rows = await storage.ReferencingPlacementBoundsAsync(typeof(ProceduralModel), modelId).ConfigureAwait(false);
        return rows.Count;
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

    private void DrawFooter(ProceduralModel model, int loaded, int stored)
    {
        ImGui.Spacing();
        if (ImGui.SmallButton("Duplicate"))
        {
            Duplicate(model);
        }

        ImGui.SameLine();
        if (stored > 0)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.SmallButton("Delete") && stored == 0)
        {
            Delete(model);
        }

        if (stored > 0)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(loaded == stored
                ? $"in use by {stored} placements"
                : $"in use by {stored} placements ({loaded} loaded)");
        }
    }

    private void Duplicate(ProceduralModel model)
    {
        var clone = new ProceduralModel
        {
            Name = UniqueName($"{model.Name} Copy", Models.Models.Select(m => m.Name)),
            FunctionId = model.FunctionId,
            Parameters = model.Parameters,
            Formats = model.Formats,
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
