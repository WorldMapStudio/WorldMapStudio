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
    private SceneEntity[] _dragged = [];

    private readonly List<SceneEntity> _listed = [];
    private readonly HashSet<SceneEntity> _visible = [];
    private readonly List<(SceneEntity Entity, int Depth)> _rows = [];
    private readonly HashSet<SceneEntity> _flattened = [];
    private int _listedVersion = -1;

    public OutlineWindow(WindowManager manager)
        : base("Outline", defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
        _sessions = manager.Context.EditSessions;
    }

    protected override void DrawContent()
    {
        IReadOnlyList<SceneEntity> entities = Listed();
        if (entities.Count == 0)
        {
            ImGui.TextDisabled("No entities loaded.");
            return;
        }

        Flatten(entities);

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
    // Rebuilt every frame because parenting changes without the registry's version moving.
    private void Flatten(IReadOnlyList<SceneEntity> entities)
    {
        _rows.Clear();
        _flattened.Clear();
        foreach (SceneEntity entity in entities)
        {
            if (entity.Parent == null || !_visible.Contains(entity.Parent))
            {
                FlattenEntity(entity, 0);
            }
        }
    }

    private void FlattenEntity(SceneEntity entity, int depth)
    {
        if (!_visible.Contains(entity) || !_flattened.Add(entity))
        {
            return;
        }

        _rows.Add((entity, depth));
        if (!HasVisibleChildren(entity) || !IsExpanded(entity))
        {
            return;
        }

        foreach (SceneEntity child in entity.Children)
        {
            FlattenEntity(child, depth + 1);
        }
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

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | ImGuiTreeNodeFlags.NoTreePushOnOpen
            | (HasVisibleChildren(entity) ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.Leaf)
            | (_selection.IsSelected(entity) ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None);

        ImGui.TreeNodeEx(Label(entity), flags);
        DrawDragSource(entity);
        DrawParentDropTarget(entity);

        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
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

        if (indent > 0.0f)
        {
            ImGui.Unindent(indent);
        }
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
