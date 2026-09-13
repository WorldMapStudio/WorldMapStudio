using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// One "currently editing a catalog" window for every browsable catalog (<see cref="ICatalogBrowser"/>),
/// instead of a bespoke window per table: pick a catalog, search it (cheap, untracked), open a result to
/// edit it (undo/session/commit-tracked, via whichever of <see cref="ICatalogEntityFactory"/>/
/// <see cref="ILazyCatalogEntityFactory"/> that catalog actually is), and follow links a catalog draws to
/// another one — see <see cref="ICatalogBrowser.DrawFields"/> for why link-following is each
/// catalog's own responsibility rather than something this shell knows about.
///
/// Behaves like a browser tab, not a multi-document editor: exactly one thing is ever on screen — either
/// a catalog's search page, or one open entity — never both, and never more than one entity at a time.
/// Opening something (a search result, or a link another catalog drew) navigates there and pushes the
/// view you left onto <see cref="_history"/>, so Back is a literal "go to what I was just looking at".
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class CatalogBrowserWindow : Window
{
    public override string? Category => "Catalogs";

    private readonly record struct BrowserView(ICatalogBrowser Catalog, CatalogEntity? Entity);

    private readonly EditorContext _context;
    private readonly List<ICatalogBrowser> _catalogs;
    private readonly FieldEditTracker _tracker = new();
    private readonly Stack<BrowserView> _history = new();

    private ICatalogBrowser? _current;
    private CatalogEntity? _openEntity;
    private string _query = string.Empty;
    private string _fieldFilter = string.Empty;
    private IReadOnlyList<CatalogSearchResult> _results = [];
    private string? _status;

    public CatalogBrowserWindow(WindowManager manager)
        : base("Catalog Browser", startOpen: false, defaultSize: new NVector2(700.0f, 620.0f))
    {
        _context = manager.Context;
        _catalogs = _context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .OrderBy(catalog => catalog.CatalogName).ToList();
        _current = _catalogs.FirstOrDefault();
    }

    protected override void DrawContent()
    {
        DrawCatalogPicker();

        if (_current is null)
        {
            ImGui.TextDisabled("No browsable catalogs registered.");
            return;
        }

        ImGui.Spacing();

        if (_openEntity is not null)
        {
            DrawEntity(_current, _openEntity);
        }
        else
        {
            DrawSearch(_current);
        }
    }

    private void DrawCatalogPicker()
    {
        if (_history.Count > 0)
        {
            if (ImGui.SmallButton("< Back"))
            {
                Back();
            }

            ImGui.SameLine();
        }

        string label = _current?.CatalogName ?? "(none)";
        ImGui.SetNextItemWidth(260.0f);
        if (ImGui.BeginCombo("Catalog", label))
        {
            foreach (ICatalogBrowser catalog in _catalogs)
            {
                if (ImGui.Selectable(catalog.CatalogName, catalog == _current))
                {
                    NavigateTo(catalog, entity: null);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawSearch(ICatalogBrowser catalog)
    {
        ImGui.SetNextItemWidth(-80.0f);
        bool searched = ImGui.InputTextWithHint("##query", "Search...", ref _query, 128);
        ImGui.SameLine();
        searched |= ImGui.Button("Search");

        if (searched)
        {
            Search(catalog);
        }

        catalog.DrawCreate(_context, entity => NavigateTo(catalog, entity));

        if (_status is not null)
        {
            ImGui.TextDisabled(_status);
        }

        if (_results.Count == 0)
        {
            return;
        }

        if (!ImGui.BeginTable("CatalogBrowserResults", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                new NVector2(0.0f, 160.0f)))
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
                Open(catalog, result.Key);
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawEntity(ICatalogBrowser catalog, CatalogEntity entity)
    {
        ImGui.TextDisabled(entity.DisplayName);

        ImGuiEx.FieldFilterInput("##fieldfilter", ref _fieldFilter);
        ImGui.Spacing();

        catalog.DrawFields(_context, entity, _tracker, Navigate, _fieldFilter);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Close"))
        {
            CloseView();
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            Delete(entity);
        }
    }

    // Called by a catalog's own DrawFields when a field is a reference to another catalog's row.
    private void Navigate(string catalogName, string key)
    {
        ICatalogBrowser? target = _catalogs.FirstOrDefault(catalog => catalog.CatalogName == catalogName);
        if (target is null)
        {
            _status = $"Unknown catalog '{catalogName}'.";
            return;
        }

        Open(target, key);
    }

    private void Search(ICatalogBrowser catalog)
    {
        _results = BlockingWork.Run(() => catalog.SearchAsync(_query));
        _status = _results.Count == 0 ? "No matches." : $"{_results.Count} match(es).";
    }

    /// <summary>Opens and focuses this window at <paramref name="catalogName"/>/<paramref name="key"/> —
    /// the entry point a reference field falls back to when it has no <c>navigate</c> callback of its
    /// own (an inspector window, rather than this browser's own <see cref="DrawFields"/> call). False
    /// if no such catalog is registered or the key doesn't resolve.</summary>
    public bool Open(string catalogName, string key)
    {
        ICatalogBrowser? catalog = _catalogs.FirstOrDefault(c => c.CatalogName == catalogName);
        if (catalog is null)
        {
            return false;
        }

        IsOpen = true;
        ImGui.SetWindowFocus(Title);
        return Open(catalog, key);
    }

    private bool Open(ICatalogBrowser catalog, string key)
    {
        CatalogEntity? entity = BlockingWork.Run(() => catalog.OpenAsync(_context, key));
        if (entity is null)
        {
            _status = $"'{key}' not found in {catalog.CatalogName}.";
            return false;
        }

        NavigateTo(catalog, entity);
        return true;
    }

    // Deliberately never touches CatalogEntityRegistry. A catalog entity is exactly as much a "real"
    // tracked entity as a spatial one — it doesn't get evicted just because a window stopped displaying
    // it, only ever by an explicit, undo-tracked Delete. So "Close" is pure navigation: back to this
    // catalog's own search (not a history pop, which might land somewhere entirely different — another
    // catalog reached by a followed link). The entity stays in the registry, pinned by the edit session
    // if it has unsaved changes, findable again later via OpenAsync's registry-first lookup regardless
    // of whether the browser currently has it open.
    private void CloseView() => _openEntity = null;

    private void Delete(CatalogEntity entity)
    {
        var command = new DeleteCatalogEntityCommand(_context.Catalog, entity);
        command.Apply();
        _context.EditSessions.Record(command);
        _openEntity = null;
    }

    private void NavigateTo(ICatalogBrowser catalog, CatalogEntity? entity)
    {
        if (_current is not null)
        {
            _history.Push(new BrowserView(_current, _openEntity));
        }

        SetView(catalog, entity);
    }

    private void Back()
    {
        if (_history.TryPop(out BrowserView previous))
        {
            SetView(previous.Catalog, previous.Entity);
        }
    }

    private void SetView(ICatalogBrowser catalog, CatalogEntity? entity)
    {
        if (catalog != _current)
        {
            _query = string.Empty;
            _results = [];
            _status = null;
        }

        _fieldFilter = string.Empty;

        _current = catalog;
        _openEntity = entity;
    }
}
