using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// The built-in <see cref="ICatalogSearchView"/> every catalog gets: a plain "key / name" table for
/// <see cref="CatalogSearchPurpose.Browse"/>, a search-as-you-type <c>Selectable</c> list for
/// <see cref="CatalogSearchPurpose.Pick"/> — merged from what <c>CatalogBrowserWindow.DrawSearch</c> and
/// <c>CatalogEntitySelectionOperation.DrawList</c> each drew before views existed. Lowest
/// <see cref="Priority"/> on purpose: any catalog that gains a second view still opens on List until the
/// user switches, so nothing changes for anyone who hasn't opted in.
/// </summary>
[Subsystem(nameof(EditorStorage))]
public sealed class CatalogListSearchView : ICatalogSearchView
{
    // The subsystem generator always calls "new Subsystem(parent)" — an unused parameter is the
    // cheapest way to satisfy that without a body-less constructor of its own.
    public CatalogListSearchView(EditorStorage storage)
    {
    }

    public string ViewName => "List";

    public bool Supports(ICatalogBrowser catalog) => true;

    public Vector2 PreferredPickerSize => new(460.0f, 320.0f);

    public ICatalogSearchViewSession CreateSession(CatalogSearchViewHost host) => new Session(host);

    private sealed class Session : ICatalogSearchViewSession
    {
        private readonly CatalogSearchViewHost _host;
        private IReadOnlyList<CatalogSearchResult> _results = [];
        private string? _status;
        private bool _searched;

        public Session(CatalogSearchViewHost host)
        {
            _host = host;
            Filter = host.InitialFilter;
        }

        public string Filter { get; set; }

        public void Draw(Vector2 available)
        {
            if (_host.Purpose == CatalogSearchPurpose.Pick)
            {
                DrawPick(available);
            }
            else
            {
                DrawBrowse(available);
            }
        }

        public string? LabelFor(string key) =>
            _results.FirstOrDefault(result => result.Key == key) is { } match ? match.Label : null;

        private void DrawBrowse(Vector2 available)
        {
            float startY = ImGui.GetCursorPosY();

            ImGui.SetNextItemWidth(-80.0f);
            string filter = Filter;
            bool searched = ImGui.InputTextWithHint("##query", "Search...", ref filter, 128);
            Filter = filter;
            ImGui.SameLine();
            searched |= ImGui.Button("Search");

            if (!_searched || searched)
            {
                RunSearch();
            }

            if (_status is not null)
            {
                ImGui.TextDisabled(_status);
            }

            if (_results.Count == 0)
            {
                return;
            }

            float tableHeight = MathF.Max(120.0f, available.Y - (ImGui.GetCursorPosY() - startY));
            if (!ImGui.BeginTable("CatalogSearchResults", 3,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                    new Vector2(0.0f, tableHeight)))
            {
                return;
            }

            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableHeadersRow();

            foreach (CatalogSearchResult result in _results)
            {
                ImGui.PushID(result.Key);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(result.Key);

                ImGui.TableNextColumn();
                ImGui.Text(result.Text);

                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Open"))
                {
                    _host.Activate(result.Key);
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        private void DrawPick(Vector2 available)
        {
            float startY = ImGui.GetCursorPosY();

            ImGui.SetNextItemWidth(available.X);
            string filter = Filter;
            if (ImGui.InputTextWithHint("##filter", "Search...", ref filter, 128))
            {
                Filter = filter;
                RunSearch();
            }

            if (!_searched)
            {
                RunSearch();
            }

            float listHeight = MathF.Max(80.0f, available.Y - (ImGui.GetCursorPosY() - startY));
            ImGui.BeginChild("CatalogSearchPickList", new Vector2(available.X, listHeight), true, ImGuiWindowFlags.None);

            if (_results.Count == 0 && !_searched)
            {
                ImGui.TextDisabled("Searching...");
            }
            else if (_results.Count == 0)
            {
                ImGui.TextDisabled("No matches.");
            }
            else
            {
                string selected = _host.SelectedKey();
                foreach (CatalogSearchResult result in _results)
                {
                    if (ImGui.Selectable($"{result.Label}##{result.Key}", result.Key == selected))
                    {
                        _host.Highlight(result.Key);
                    }

                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _host.Activate(result.Key);
                    }
                }
            }

            ImGui.EndChild();
        }

        private void RunSearch()
        {
            _searched = true;
            _results = BlockingWork.Run(() => _host.Catalog.SearchAsync(Filter));
            if (_host.Purpose == CatalogSearchPurpose.Browse)
            {
                _status = _results.Count == 0 ? "No matches." : $"{_results.Count} match(es).";
            }
        }

        public void Dispose()
        {
        }
    }
}
