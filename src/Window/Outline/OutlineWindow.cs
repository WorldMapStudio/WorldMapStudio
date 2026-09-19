using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the loaded scene entities and mirrors the shared selection: clicking an entry selects it
/// (Ctrl/Shift to add or remove), and entities selected in the viewport show as highlighted here.
/// Derived entities (landscape chunks) are excluded — they live in the <see cref="ChunksWindow"/>
/// instead, since there can be many of them.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class OutlineWindow : Window
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.O, ShortcutModifiers.Alt);

    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;
    private readonly ViewCategorySystem _viewCategories;

    private readonly List<SceneEntity> _listed = [];
    private readonly List<SceneEntity> _rows = [];
    private int _listedVersion = -1;
    private int _rowsListedVersion = -1;
    private bool _rowsDirty = true;
    private string _filter = string.Empty;
    private string[] _filterTokens = [];

    public OutlineWindow(WindowManager manager)
        : base("Outline", defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
        _viewCategories = manager.Context.ViewCategories;
    }

    protected override void DrawContent()
    {
        DrawFilter();
        ImGui.Separator();

        // The list scrolls in its own child region so the filter box above stays put rather than
        // scrolling out of view with the rows.
        if (!ImGui.BeginChild("outline-scroll", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        DrawList();
        ImGui.EndChild();
    }

    private void DrawList()
    {
        IReadOnlyList<SceneEntity> entities = Listed();
        if (entities.Count == 0)
        {
            ImGui.TextDisabled("No entities loaded.");
            return;
        }

        BuildRows();

        if (_rows.Count == 0)
        {
            ImGui.TextDisabled("Nothing matches the filter.");
            return;
        }

        // Drawn through a clipper: ImGui pays a row's per-item cost whether or not it is on screen, and
        // an imported map lists thousands of them, which was the single largest thing the editor did per frame.
        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_rows.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    DrawEntity(_rows[i]);
                }
            }

            clipper.End();
            clipper.Destroy();
        }
    }

    private void DrawFilter()
    {
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.InputTextWithHint("##filter", "Filter by name...", ref _filter, 128))
        {
            _filterTokens = _filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            _rowsDirty = true;
        }
    }

    // What the outline lists, rebuilt only when the registry says its membership moved — neither what
    // is in view nor what counts as derived can change without that.
    private IReadOnlyList<SceneEntity> Listed()
    {
        if (_listedVersion == _scene.Version)
        {
            return _listed;
        }

        _listedVersion = _scene.Version;
        _listed.Clear();
        foreach (SceneEntity entity in _scene.InView)
        {
            if (entity is not IDerivedEntity)
            {
                _listed.Add(entity);
            }
        }

        return _listed;
    }

    // The rows to draw: the listed entities that pass the filter. Held across frames rather than
    // refiltered each one, since the scan is over everything loaded and not over what is on screen.
    private void BuildRows()
    {
        if (!_rowsDirty && _rowsListedVersion == _listedVersion)
        {
            return;
        }

        _rowsDirty = false;
        _rowsListedVersion = _listedVersion;

        _rows.Clear();
        foreach (SceneEntity entity in _listed)
        {
            if (MatchesFilter(entity))
            {
                _rows.Add(entity);
            }
        }
    }

    private bool MatchesFilter(SceneEntity entity)
    {
        foreach (string token in _filterTokens)
        {
            if (entity.DisplayName.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private void DrawEntity(SceneEntity entity)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.NoTreePushOnOpen
            | (_selection.IsSelected(entity) ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None);

        // Greyed rather than left out or disabled: the row must stay clickable, since the outline is
        // how a hidden entity (a light with lighting off, say) stays reachable at all.
        IViewCategory? hidingCategory = HidingCategory(entity);
        if (hidingCategory != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        }

        ImGui.TreeNodeEx(Label(entity), flags);

        if (hidingCategory != null)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemClicked())
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (io.KeyCtrl || io.KeyShift)
            {
                _selection.Toggle(entity);
            }
            else
            {
                _selection.Set(entity);
            }
        }

        DrawVisibilityToggle(entity, hidingCategory);
    }

    // The category currently hiding this entity, or null when none is. Drawn after IsItemClicked has
    // already read the tree node as the "last item", so it cannot disturb row selection.
    private void DrawVisibilityToggle(SceneEntity entity, IViewCategory? hidingCategory)
    {
        IViewCategory? ownCategory = hidingCategory ?? _viewCategories.All.FirstOrDefault(category => category.Includes(entity));
        if (ownCategory == null)
        {
            return;
        }

        bool hidden = hidingCategory != null;
        ImGui.SameLine();
        if (ImGui.SmallButton($"{(hidden ? "○" : "●")}##vis{entity.Id.Value}"))
        {
            _viewCategories.SetHidden(ownCategory.Id, !hidden);
        }
    }

    // The first hidden category (in priority order) that includes this entity, or null when none is
    // — the union rule ViewCategorySystem.IsHidden(SceneEntity) itself applies, but this also needs to
    // know *which* category to hand back to DrawVisibilityToggle.
    private IViewCategory? HidingCategory(SceneEntity entity)
    {
        foreach (IViewCategory category in _viewCategories.All)
        {
            if (_viewCategories.IsHidden(category.Id) && category.Includes(entity))
            {
                return category;
            }
        }

        return null;
    }

    private static string Label(SceneEntity entity) => $"{entity.DisplayName}##{entity.Id.Value}";
}
