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
    private const string DragDropPayload = "WMS_SCENE_ENTITY";

    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;
    private readonly EditSessionManager _sessions;
    private SceneEntity[] _dragged = [];

    public OutlineWindow(WindowManager manager)
        : base("Outline", defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Context.Scene;
        _selection = manager.Context.Selection;
        _sessions = manager.Context.EditSessions;
    }

    protected override void DrawContent()
    {
        List<SceneEntity> entities = _scene.InView.Where(entity => entity is not IDerivedEntity).ToList();
        if (entities.Count == 0)
        {
            ImGui.TextDisabled("No entities loaded.");
            return;
        }

        var visible = entities.ToHashSet();
        var drawn = new HashSet<SceneEntity>();
        foreach (SceneEntity entity in entities.Where(entity => entity.Parent == null || !visible.Contains(entity.Parent)))
        {
            DrawEntity(entity, visible, drawn);
        }

        DrawRootDropTarget();
    }

    private void DrawEntity(SceneEntity entity, IReadOnlySet<SceneEntity> visible, HashSet<SceneEntity> drawn)
    {
        if (!visible.Contains(entity) || !drawn.Add(entity))
        {
            return;
        }

        bool hasVisibleChildren = entity.Children.Any(visible.Contains);
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow
            | ImGuiTreeNodeFlags.SpanAvailWidth
            | (hasVisibleChildren ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.Leaf)
            | (_selection.IsSelected(entity) ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None);

        bool open = ImGui.TreeNodeEx($"{entity.DisplayName}##{entity.Id.Value}", flags);
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

        if (!open)
        {
            return;
        }

        foreach (SceneEntity child in entity.Children)
        {
            DrawEntity(child, visible, drawn);
        }

        ImGui.TreePop();
    }

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
