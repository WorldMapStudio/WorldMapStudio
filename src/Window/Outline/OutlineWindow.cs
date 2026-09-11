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
/// instead, since there can be many of them and they never parent or get parented.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class OutlineWindow : Window
{
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.O, ShortcutModifiers.Alt);

    private const string DragDropPayload = "WMS_SCENE_ENTITY";

    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;
    private readonly EditSessionManager _sessions;
    private readonly ViewCategorySystem _viewCategories;
    private SceneEntity[] _dragged = [];

    private readonly List<SceneEntity> _listed = [];
    private readonly HashSet<SceneEntity> _visible = [];
    private readonly HashSet<SceneEntity> _keep = [];
    private readonly List<(SceneEntity Entity, int Depth)> _rows = [];
    private int _listedVersion = -1;
    private int _rowsListedVersion = -1;
    private int _rowsHierarchyVersion = -1;
    private bool _rowsDirty = true;
    private string _filter = string.Empty;
    private string[] _filterTokens = [];

    public OutlineWindow(WindowManager manager)
        : base("Outline", defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
        _sessions = manager.Context.EditSessions;
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

        Flatten();

        if (_rows.Count == 0)
        {
            ImGui.TextDisabled("Nothing matches the filter.");
            return;
        }

        // Rows are flattened first and drawn through a clipper rather than recursed into directly:
        // ImGui pays a tree node's per-item cost whether or not the row is on screen, and an imported
        // map lists thousands of them, which was the single largest thing the editor did per frame.
        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_rows.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    DrawEntity(_rows[i].Entity, _rows[i].Depth);
                }
            }

            clipper.End();
            clipper.Destroy();
        }

        DrawRootDropTarget();
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
        _visible.Clear();
        foreach (SceneEntity entity in _scene.InView)
        {
            if (entity is not IDerivedEntity)
            {
                _listed.Add(entity);
                _visible.Add(entity);
            }
        }

        return _listed;
    }

    // The rows the tree would draw, in order, descending only into nodes that are actually open.
    //
    // Held across frames rather than rebuilt each one: the walk is over everything loaded, not over
    // what is on screen, so at a real view distance rewalking it per frame costs more than drawing the
    // tree ever did. Only three things can change the answer — what is loaded (the registry version),
    // how entities are parented (the hierarchy version) and which nodes the user has folded open
    // (_rowsDirty, set when a node toggles or the filter text changes).
    private void Flatten()
    {
        if (!_rowsDirty && _rowsListedVersion == _listedVersion && _rowsHierarchyVersion == SceneEntity.HierarchyVersion)
        {
            return;
        }

        _rowsDirty = false;
        _rowsListedVersion = _listedVersion;
        _rowsHierarchyVersion = SceneEntity.HierarchyVersion;

        _rows.Clear();

        if (_filterTokens.Length > 0)
        {
            BuildKeepSet();
            foreach (SceneEntity entity in _listed)
            {
                if (_keep.Contains(entity) && (entity.Parent == null || !_visible.Contains(entity.Parent)))
                {
                    FlattenFiltered(entity, 0);
                }
            }

            return;
        }

        foreach (SceneEntity entity in _listed)
        {
            // A listed entity is either a root here or reached below as some visible parent's child,
            // never both, and SceneEntity.Parent refuses to build a cycle — so no "already drawn" set
            // is needed to keep a row from appearing twice.
            if (entity.Parent == null || !_visible.Contains(entity.Parent))
            {
                FlattenEntity(entity, 0);
            }
        }
    }

    private void FlattenEntity(SceneEntity entity, int depth)
    {
        _rows.Add((entity, depth));
        if (!HasVisibleChildren(entity) || !IsExpanded(entity))
        {
            return;
        }

        foreach (SceneEntity child in entity.Children)
        {
            if (_visible.Contains(child))
            {
                FlattenEntity(child, depth + 1);
            }
        }
    }

    // Every match plus its visible ancestors, so a filtered tree still shows where a match lives
    // instead of just the leaf that matched.
    private void BuildKeepSet()
    {
        _keep.Clear();
        foreach (SceneEntity entity in _listed)
        {
            if (!MatchesFilter(entity))
            {
                continue;
            }

            for (SceneEntity? current = entity; current != null && _visible.Contains(current) && _keep.Add(current); current = current.Parent)
            {
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

    // Filtered rows always descend into kept children regardless of fold state — the filter's whole
    // point is to surface matches the user hasn't expanded to yet.
    private void FlattenFiltered(SceneEntity entity, int depth)
    {
        _rows.Add((entity, depth));
        foreach (SceneEntity child in entity.Children)
        {
            if (_keep.Contains(child))
            {
                FlattenFiltered(child, depth + 1);
            }
        }
    }

    private bool HasKeptChildren(SceneEntity entity)
    {
        foreach (SceneEntity child in entity.Children)
        {
            if (_keep.Contains(child))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawEntity(SceneEntity entity, int depth)
    {
        // Depth is drawn as an explicit indent, and the node pushes neither an id nor an indent of its
        // own (NoTreePushOnOpen): a clipped row has no ancestor row on screen to have pushed them, and
        // an id that does not depend on the ancestry is also what lets Flatten read a node's open state
        // before deciding whether to descend.
        float indent = depth * ImGui.GetStyle().IndentSpacing;
        if (indent > 0.0f)
        {
            ImGui.Indent(indent);
        }

        bool filtering = _filterTokens.Length > 0;
        bool hasChildren = filtering ? HasKeptChildren(entity) : HasVisibleChildren(entity);
        if (filtering && hasChildren)
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.NoTreePushOnOpen
            | (hasChildren ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.Leaf)
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

        DrawDragSource(entity);
        DrawParentDropTarget(entity);

        bool toggled = ImGui.IsItemToggledOpen();
        _rowsDirty |= toggled;

        if (ImGui.IsItemClicked() && !toggled)
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

        if (indent > 0.0f)
        {
            ImGui.Unindent(indent);
        }
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

    // A plain loop rather than Any(_visible.Contains): this runs for every listed entity on every
    // frame, and the method group would allocate a delegate and an enumerator on each one.
    private bool HasVisibleChildren(SceneEntity entity)
    {
        foreach (SceneEntity child in entity.Children)
        {
            if (_visible.Contains(child))
            {
                return true;
            }
        }

        return false;
    }

    // A node with children defaults to open, matching the DefaultOpen flag DrawEntity gives it.
    private bool IsExpanded(SceneEntity entity) =>
        ImGui.GetStateStorage().GetInt(ImGui.GetID(Label(entity)), 1) != 0;

    private static string Label(SceneEntity entity) => $"{entity.DisplayName}##{entity.Id.Value}";

    private void DrawDragSource(SceneEntity entity)
    {
        if (entity is IDerivedEntity || !ImGui.BeginDragDropSource())
        {
            return;
        }

        _dragged = DraggedEntities(entity).ToArray();
        ImGui.SetDragDropPayload(DragDropPayload, System.IntPtr.Zero, 0);
        ImGui.Text(_dragged.Length == 1 ? entity.DisplayName : $"{_dragged.Length} entities");
        ImGui.EndDragDropSource();
    }

    private IEnumerable<SceneEntity> DraggedEntities(SceneEntity entity)
    {
        if (!_selection.IsSelected(entity))
        {
            yield return entity;
            yield break;
        }

        foreach (IEntity selected in _selection.Selected)
        {
            if (selected is SceneEntity scene && scene is not IDerivedEntity)
            {
                yield return scene;
            }
        }
    }

    private void DrawParentDropTarget(SceneEntity parent)
    {
        if (parent is IDerivedEntity || !ImGui.BeginDragDropTarget())
        {
            return;
        }

        if (AcceptSceneEntityDrop())
        {
            Reparent(_dragged, parent);
        }

        ImGui.EndDragDropTarget();
    }

    private void DrawRootDropTarget()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        ImGui.Dummy(new Vector2(available.X, System.MathF.Max(available.Y, 12.0f)));
        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        if (AcceptSceneEntityDrop())
        {
            Reparent(_dragged, null);
        }

        ImGui.EndDragDropTarget();
    }

    private void Reparent(IReadOnlyList<SceneEntity> entities, SceneEntity? parent)
    {
        var dragged = entities.ToHashSet();
        List<SetSceneEntityParentCommand> commands = [];
        foreach (SceneEntity entity in entities.Where(entity => entity.Parent == null || !dragged.Contains(entity.Parent)))
        {
            if (entity is IDerivedEntity
                || ReferenceEquals(entity.Parent, parent)
                || !SetSceneEntityParentCommand.CanParentTo(entity, parent))
            {
                continue;
            }

            commands.Add(new SetSceneEntityParentCommand(entity, entity.Parent, parent));
        }

        if (commands.Count == 0)
        {
            return;
        }

        foreach (SetSceneEntityParentCommand command in commands)
        {
            command.Apply();
        }

        _sessions.Record(commands.Count == 1
            ? commands[0]
            : new BatchEditCommand(parent == null
                ? $"Clear parent on {commands.Count} entities"
                : $"Parent {commands.Count} entities to {parent.DisplayName}",
                commands));
    }

    private static unsafe bool AcceptSceneEntityDrop() =>
        ImGui.AcceptDragDropPayload(DragDropPayload).NativePtr != null;
}
