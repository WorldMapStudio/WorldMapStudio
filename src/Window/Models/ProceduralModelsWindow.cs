using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors <see cref="ProceduralModel"/>s — the catalog of procedural meshes a
/// <see cref="ProceduralComponent"/> references by id. A thin wrapper over <see cref="ProceduralModelFactory"/>'s
/// <see cref="ICatalogBrowser"/> capability, the same one <see cref="CatalogBrowserWindow"/> draws
/// against, but locked to this one catalog and without its cross-catalog history stack — a link into
/// another catalog has nowhere to go from here, so it reports where to follow it instead.
///
/// A list of every loaded model can't answer "what models are there" because the catalog is lazily
/// loaded (see <see cref="ProceduralModelFactory"/>) — most rows are never loaded at all — so this
/// searches storage instead, the same way <see cref="CatalogBrowserWindow"/> does.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ProceduralModelsWindow : Window
{
    public override string? Category => "Models";

    private readonly EditorContext _context;
    private readonly ProceduralModelFactory _catalog;
    private readonly FieldEditTracker _tracker = new();

    private string _query = string.Empty;
    private string _fieldFilter = string.Empty;
    private IReadOnlyList<CatalogSearchResult> _results = [];
    private Task<IReadOnlyList<CatalogSearchResult>>? _searchTask;
    private string? _status;
    private CatalogEntity? _openEntity;

    // Stored placements — not loaded ones — are what makes Delete safe: a model whose only placements
    // are streamed out has zero loaded uses but still has rows referencing it in storage. Queried once
    // per open model per (catalog, commit) pair and cached: stored placements only change on commit, so
    // streaming (which bumps Scene.Version on every scan) must not invalidate it. Null while a query is
    // in flight or hasn't started yet — the footer shows "…" and keeps Delete disabled for that.
    private int? _storedPlacementCountFor;
    private (int Catalog, int Commits) _storedPlacementCountVersion = (-1, -1);
    private int? _storedPlacementCount;
    private Task<int>? _storedPlacementCountTask;

    public ProceduralModelsWindow(WindowManager manager)
        : base("Procedural Models", startOpen: false, defaultSize: new Vector2(560.0f, 560.0f))
    {
        _context = manager.Context;
        _catalog = _context.Database.EditorStorage.ProceduralModelFactory;
    }

    protected override void DrawContent()
    {
        if (_openEntity is not null)
        {
            DrawEntity((ProceduralModel)_openEntity);
        }
        else
        {
            DrawSearch();
        }
    }

    private void DrawSearch()
    {
        ImGui.SetNextItemWidth(-80.0f);
        bool searched = ImGui.InputTextWithHint("##query", "Search...", ref _query, 128);
        ImGui.SameLine();
        searched |= ImGui.Button("Search");

        if (searched)
        {
            Search();
        }

        PumpSearch();

        _catalog.DrawCreate(_context, Open);

        if (_status is not null)
        {
            ImGui.TextDisabled(_status);
        }

        if (_results.Count == 0)
        {
            return;
        }

        if (!ImGui.BeginTable("ProceduralModelResults", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                new Vector2(0.0f, 300.0f)))
        {
            return;
        }

        ImGui.TableSetupColumn("Result");
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 50.0f);
        ImGui.TableHeadersRow();

        foreach (CatalogSearchResult result in _results)
        {
            ImGui.PushID(result.Key);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(result.Label);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Open"))
            {
                OpenByKey(result.Key);
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawEntity(ProceduralModel model)
    {
        ImGui.TextDisabled($"Id #{model.RecordId}");

        ImGuiEx.FieldFilterInput("##fieldfilter", ref _fieldFilter);
        ImGui.Spacing();

        _catalog.DrawFields(_context, model, _tracker, Navigate, _fieldFilter);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawFooter(model);
    }

    private void DrawFooter(ProceduralModel model)
    {
        if (ImGui.Button("Close"))
        {
            _openEntity = null;
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Duplicate"))
        {
            Duplicate(model);
        }

        int loaded = _context.Procedural.UsageCount(model.RecordId ?? -1);
        int? stored = StoredPlacementCount(model);

        ImGui.SameLine();
        bool blocked = stored is not 0;
        if (blocked)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Delete") && stored == 0)
        {
            Delete(model);
        }

        if (blocked)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled(stored is not int count
                ? "…"
                : loaded == count
                    ? $"in use by {count} placements"
                    : $"in use by {count} placements ({loaded} loaded)");
        }
    }

    // Only one catalog is browsable here — a link another catalog's field drew has nowhere to go from
    // this window, unlike CatalogBrowserWindow, which hosts every browsable catalog at once.
    private void Navigate(string catalogName, string key) =>
        _status = $"'{catalogName}' #{key} — open the Catalog Browser to follow this link.";

    private void Search()
    {
        string query = _query;
        _searchTask = BackgroundWork.Run(() => _catalog.SearchAsync(query));
        _status = "Searching…";
    }

    // A later Search() simply overwrites _searchTask, so a stale in-flight one is never applied — its
    // result lands, if ever, on a field nothing reads any more.
    private void PumpSearch()
    {
        if (_searchTask is not { IsCompleted: true } completed)
        {
            return;
        }

        _searchTask = null;
        if (completed.IsCompletedSuccessfully)
        {
            _results = completed.Result;
            _status = _results.Count == 0 ? "No matches." : $"{_results.Count} match(es).";
        }
        else
        {
            _status = $"Search failed: {completed.Exception?.GetBaseException().Message}";
        }
    }

    private void OpenByKey(string key)
    {
        CatalogEntity? entity = BlockingWork.Run(() => _catalog.OpenAsync(_context, key));
        if (entity is null)
        {
            _status = $"'{key}' not found.";
            return;
        }

        Open(entity);
    }

    private void Open(CatalogEntity entity)
    {
        _fieldFilter = string.Empty;
        _openEntity = entity;
    }

    /// <summary>Every stored placement referencing this model, regardless of whether it is currently
    /// loaded — what actually makes deleting the model safe. Null while unknown: not yet queried for
    /// this model/version, or the query is still in flight.</summary>
    private int? StoredPlacementCount(ProceduralModel model)
    {
        if (model.RecordId is not int id)
        {
            return 0;
        }

        var versions = (_context.Catalog.Version, _context.EditSessions.CommitCount);
        if (_storedPlacementCountFor != id || _storedPlacementCountVersion != versions)
        {
            _storedPlacementCountFor = id;
            _storedPlacementCountVersion = versions;
            _storedPlacementCount = null;
            _storedPlacementCountTask = BackgroundWork.Run(() => CountStoredPlacementsAsync(id));
        }

        if (_storedPlacementCountTask is { IsCompleted: true } completed)
        {
            _storedPlacementCountTask = null;
            _storedPlacementCount = completed.IsCompletedSuccessfully ? completed.Result : 0;
        }

        return _storedPlacementCount;
    }

    private Task<int> CountStoredPlacementsAsync(int modelId) =>
        _context.Database.EditorStorage.CountReferencingPlacementsAsync(typeof(ProceduralModel), modelId);

    private void Duplicate(ProceduralModel model)
    {
        var clone = new ProceduralModel
        {
            Name = $"{model.Name} Copy",
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
        Open(clone);
    }

    private void Delete(ProceduralModel model)
    {
        var command = new DeleteCatalogEntityCommand(_context.Catalog, model);
        command.Apply();
        _context.EditSessions.Record(command);
        _openEntity = null;
    }
}
