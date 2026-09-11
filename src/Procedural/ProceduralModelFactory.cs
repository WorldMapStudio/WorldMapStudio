using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImGuiNET;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// Maps <see cref="ProceduralModel"/> to and from the Editor storage's <c>wms_procedural_models</c>
/// table. Lazy rather than eager: a model is loaded because a placement needs it, resolved by the scan
/// that loads that placement (<see cref="ProceduralComponentPersistence.LoadAsync"/>) through
/// <see cref="EditorKeyedCatalogFactory{TEntity,TRecord}.LoadByIdAsync"/> rather than all at once.
///
/// Also implements <see cref="ICatalogBrowser"/> — the same two-capabilities-on-one-class pairing every
/// other lazy catalog uses — so the model catalog is searchable and openable from storage through
/// <see cref="CatalogBrowserWindow"/> instead of assuming the whole (no longer loaded) set fits in a
/// list.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class ProceduralModelFactory(EditorStorage storage)
    : EditorLazyCatalogFactory<ProceduralModel, ProceduralModelRecord>(storage), ICatalogBrowser
{
    private const uint NameMaxLength = 128;
    private const int SearchLimit = 50;

    private ProceduralModelFieldEditor? _fieldEditor;
    private ProceduralModelPicker? _createPicker;

    // OpenAsync behaves like the browser it feeds: one row open for editing at a time. Retaining that
    // row (rather than every row ever opened this session) keeps the eviction sweep from dropping it
    // out from under the window drawing it, without leaking a retain for every row a session has ever
    // visited.
    private IDisposable? _openRetain;

    protected override string TableName => "wms_procedural_models";

    public string CatalogName => "Procedural Model";

    protected override ProceduralModel ToEntity(ProceduralModelRecord record)
    {
        var entity = new ProceduralModel
        {
            RecordId = record.Id,
            Name = record.Name,
            FunctionId = record.FunctionId,
            Parameters = record.Parameters,
            Formats = record.Formats,
            Materials = record.Materials,
        };
        entity.LoadNetwork(VertexNetwork.Parse(record.NetworkJson));
        return entity;
    }

    protected override void WriteRecord(ProceduralModel entity, ProceduralModelRecord record)
    {
        record.Name = entity.Name;
        record.FunctionId = entity.FunctionId;
        record.Parameters = entity.Parameters;
        record.Formats = entity.Formats;
        record.Materials = entity.Materials;
        record.NetworkJson = entity.NetworkJson;
    }

    /// <summary>A page of matches by exact id or a name substring — never materializes a network, unlike
    /// a loaded <see cref="ProceduralModel"/>.</summary>
    public async Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(string filter)
    {
        filter = filter.Trim();

        await using EditorDbContext context = Storage.CreateContext();
        IQueryable<ProceduralModelRecord> query = context.Set<ProceduralModelRecord>().AsNoTracking();

        query = int.TryParse(filter, out int id)
            ? query.Where(record => record.Id == id)
            : filter.Length > 0
                ? query.Where(record => EF.Functions.Like(record.Name, $"%{filter}%"))
                : query;

        List<ProceduralModelRecord> rows = await query.OrderBy(record => record.Id).Take(SearchLimit)
            .ToListAsync().ConfigureAwait(false);

        return rows.Select(record => new CatalogSearchResult(
            record.Id.ToString(), $"{record.Id}: {record.Name} ({record.FunctionId})")).ToList();
    }

    public async Task<CatalogEntity?> OpenAsync(EditorContext context, string key)
    {
        if (!int.TryParse(key, out int id))
        {
            return null;
        }

        if (context.Catalog.OfType<ProceduralModel>().FirstOrDefault(model => model.RecordId == id) is { } open)
        {
            Retain(context, id);
            return open;
        }

        await using EditorDbContext db = Storage.CreateContext();
        IReadOnlyList<CatalogEntity> loaded = await LoadByIdAsync(db, [id]).ConfigureAwait(false);
        if (loaded.Count == 0)
        {
            return null;
        }

        CatalogEntity entity = loaded[0];
        context.Catalog.Add(entity);
        Retain(context, id);
        return entity;
    }

    private void Retain(EditorContext context, int id)
    {
        _openRetain?.Dispose();
        _openRetain = context.Procedural.Retain(id);
    }

    public void DrawFields(EditorContext context, CatalogEntity entity, FieldEditTracker tracker,
        Action<string, string> navigate, string fieldFilter)
    {
        var model = (ProceduralModel)entity;

        ImGui.TextDisabled($"Id #{model.RecordId}");

        string name = model.Name;
        if (ImGui.InputText("Name", ref name, NameMaxLength))
        {
            model.Name = name;
        }

        tracker.Track(context.EditSessions, model, "name", name, value => model.Name = value);

        (_fieldEditor ??= new ProceduralModelFieldEditor(context.Procedural,
            new MeshParameterEditor(new TextureAssetPicker(context.Assets), context.Landscape)))
            .Draw(context.EditSessions, model);
    }

    public void DrawCreate(EditorContext context, Action<CatalogEntity> onCreated)
    {
        ProceduralModelPicker picker = _createPicker ??= new ProceduralModelPicker(context.Procedural, context.Root);
        if (ImGui.Button("New Procedural Model"))
        {
            picker.OpenCreate(context.EditSessions, context.Catalog, model => onCreated(model));
        }

        picker.Draw();
    }

    public CatalogEntity Create(EditorContext context, string key)
    {
        if (!int.TryParse(key, out int id) || id <= 0)
        {
            throw new InvalidOperationException("Procedural model id must be a positive integer.");
        }

        if (context.Catalog.OfType<ProceduralModel>().Any(model => model.RecordId == id))
        {
            throw new InvalidOperationException($"Procedural model id {id} is already open.");
        }

        var entity = new ProceduralModel { RecordId = id };
        entity.ReplaceNetwork(ProceduralModelPicker.DefaultNetwork());

        var command = new CreateCatalogEntityCommand(context.Catalog, entity);
        command.Apply();
        context.EditSessions.Record(command);
        return entity;
    }
}
