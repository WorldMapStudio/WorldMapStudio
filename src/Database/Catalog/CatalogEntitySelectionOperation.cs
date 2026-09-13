using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Search-as-you-type body of <see cref="CatalogEntityPicker"/>'s popup, against whichever
/// <see cref="ICatalogBrowser"/> the context names — the <see cref="ICatalogBrowser.SearchAsync"/>
/// analogue of <c>ModelSelectionOperation</c>'s asset listing. Runs one <see cref="BlockingWork"/>
/// search per filter change (cheap and untracked by <see cref="ICatalogBrowser.SearchAsync"/>'s own
/// contract), the same blocking-search shape <c>CatalogBrowserWindow</c> already uses.
/// </summary>
public sealed class CatalogEntitySelectionOperation : IModalOperation<CatalogEntityPickerContext>
{
    private static readonly Vector2 BodySize = new(460, 320);

    private string _filter = "";
    private string _selectedKey = "";
    private IReadOnlyList<CatalogSearchResult>? _results;
    private bool _searched;

    public ModalOperationState Draw(CatalogEntityPickerContext context)
    {
        if (!_searched)
        {
            _selectedKey = context.CurrentKey;
            RunSearch(context);
        }

        ImGui.Text($"Select {context.Catalog.CatalogName}");
        ImGui.Separator();

        ImGui.SetNextItemWidth(BodySize.X);
        if (ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128))
        {
            RunSearch(context);
        }

        ImGui.BeginChild("CatalogEntityPickerList", BodySize, true, ImGuiWindowFlags.None);
        DrawList();
        ImGui.EndChild();

        ImGui.TextDisabled(_selectedKey.Length == 0 ? "Selected: (none)" : $"Selected: {SelectedLabel()}");

        ImGui.Separator();
        if (ImGui.Button("Clear", new Vector2(120, 0)))
        {
            context.Select("");
            return ModalOperationState.Confirmed;
        }

        ImGui.SameLine();
        bool canSelect = _selectedKey.Length > 0;
        ImGui.BeginDisabled(!canSelect);
        if (ImGui.Button("Select", new Vector2(120, 0)))
        {
            context.Select(_selectedKey);
            return ModalOperationState.Confirmed;
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalOperationState.Cancelled;
        }

        return ModalOperationState.Running;
    }

    private void DrawList()
    {
        if (_results == null)
        {
            ImGui.TextDisabled("Searching...");
            return;
        }

        if (_results.Count == 0)
        {
            ImGui.TextDisabled("No matches.");
            return;
        }

        foreach (CatalogSearchResult result in _results)
        {
            if (ImGui.Selectable($"{result.Label}##{result.Key}", result.Key == _selectedKey))
            {
                _selectedKey = result.Key;
            }
        }
    }

    // The current results page already carries the label for anything selected from it; falls back to
    // the bare key for a pre-existing selection (the field's current value) that isn't on this page.
    private string SelectedLabel() =>
        _results?.FirstOrDefault(result => result.Key == _selectedKey) is { } match ? match.Label : _selectedKey;

    private void RunSearch(CatalogEntityPickerContext context)
    {
        _searched = true;
        _results = BlockingWork.Run(() => context.Catalog.SearchAsync(_filter));
    }
}
