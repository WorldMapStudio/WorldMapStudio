using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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
///
/// The search page itself is drawn by whichever <see cref="ICatalogSearchView"/> the catalog is
/// currently showing (<see cref="EditorContext.CatalogSearchViews"/>) — this window only ever draws the
/// chrome around it: Back, the catalog combo, the view toggle (hidden when a catalog has only one view),
/// <see cref="ICatalogBrowser.DrawCreate"/>, and navigation-level status.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class CatalogBrowserWindow : Window, ILayoutPersistentWindow
{
    public override string? Category => "Catalogs";

    private readonly record struct BrowserView(ICatalogBrowser Catalog, CatalogEntity? Entity);

    private readonly EditorContext _context;
    private readonly List<ICatalogBrowser> _catalogs;
    private readonly FieldEditTracker _tracker = new();
    private readonly Stack<BrowserView> _history = new();

    private ICatalogBrowser? _current;
    private CatalogEntity? _openEntity;
    private string _fieldFilter = string.Empty;
    private string? _status;

    private ICatalogSearchViewSession? _session;
    private ICatalogBrowser? _sessionCatalog;
    private ICatalogSearchView? _sessionView;

    public CatalogBrowserWindow(WindowManager manager)
        : base("Catalog Browser", startOpen: false, defaultSize: new NVector2(700.0f, 620.0f))
    {
        _context = manager.Context;
        _catalogs = _context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .OrderBy(catalog => catalog.CatalogName).ToList();
        _current = _catalogs.FirstOrDefault();
    }

    // The view toggle is per-catalog and shared with every Load popup for that catalog (see
    // CatalogSearchViews.SetPreferred) — only the preference dictionary itself is this window's to
    // persist, since it's the one ILayoutPersistentWindow in the picture.
    JsonObject? ILayoutPersistentWindow.CaptureLayoutState()
    {
        IReadOnlyDictionary<string, string> preferred = _context.CatalogSearchViews.Snapshot();
        if (preferred.Count == 0)
        {
            return null;
        }

        var searchViews = new JsonObject();
        foreach ((string catalogName, string viewName) in preferred)
        {
            searchViews[catalogName] = viewName;
        }

        return new JsonObject { ["searchViews"] = searchViews };
    }

    void ILayoutPersistentWindow.RestoreLayoutState(JsonObject state)
    {
        if (state["searchViews"] is not JsonObject searchViews)
        {
            return;
        }

        var preferred = new Dictionary<string, string>();
        foreach ((string catalogName, JsonNode? value) in searchViews)
        {
            if (value?.GetValue<string>() is { Length: > 0 } viewName)
            {
                preferred[catalogName] = viewName;
            }
        }

        _context.CatalogSearchViews.Restore(preferred);
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
        ICatalogSearchView view = DrawViewToggle(catalog);
        EnsureSession(catalog, view);

        catalog.DrawCreate(_context, entity => NavigateTo(catalog, entity));

        if (_status is not null)
        {
            ImGui.TextDisabled(_status);
        }

        _session!.Draw(ImGui.GetContentRegionAvail());
    }

    // Drawn only when a catalog has more than one view — a catalog with just List looks exactly as it
    // did before views existed.
    private ICatalogSearchView DrawViewToggle(ICatalogBrowser catalog)
    {
        IReadOnlyList<ICatalogSearchView> views = _context.CatalogSearchViews.For(catalog);
        ICatalogSearchView current = _context.CatalogSearchViews.Preferred(catalog);
        if (views.Count <= 1)
        {
            return current;
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("View:");
        foreach (ICatalogSearchView candidate in views)
        {
            ImGui.SameLine();
            bool selected = candidate == current;
            if (ImGui.RadioButton(candidate.ViewName, selected) && !selected)
            {
                _context.CatalogSearchViews.SetPreferred(catalog, candidate);
                current = candidate;
            }
        }

        return current;
    }

    private void EnsureSession(ICatalogBrowser catalog, ICatalogSearchView view)
    {
        if (_session is not null && _sessionCatalog == catalog && _sessionView == view)
        {
            return;
        }

        string filter = _sessionCatalog == catalog ? _session?.Filter ?? string.Empty : string.Empty;
        _session?.Dispose();

        var host = new CatalogSearchViewHost(
            _context, catalog, CatalogSearchPurpose.Browse, filter,
            SelectedKey: static () => string.Empty,
            Highlight: static _ => { },
            Activate: key => Open(catalog, key));

        _session = view.CreateSession(host);
        _sessionCatalog = catalog;
        _sessionView = view;
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
            _status = null;
        }

        _fieldFilter = string.Empty;

        _current = catalog;
        _openEntity = entity;
    }
}
