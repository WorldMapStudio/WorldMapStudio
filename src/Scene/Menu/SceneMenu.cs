using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The "Scene" menu: adds entities to the current scene. Adding an empty creates it at the origin,
/// records the creation in the active edit session (so it can be undone and is persisted on commit).
/// </summary>
[Subsystem(nameof(MenuBarManager))]
public sealed class SceneMenu : IMainMenu
{
    private readonly EditorContext _context;

    public float Priority => 0.75f;

    public SceneMenu(MenuBarManager manager)
    {
        _context = manager.Context;
    }

    public void Draw()
    {
        ImGuiEx.Menu("Scene", () =>
        {
            if (ImGui.BeginMenu("Add Empty"))
            {
                AddEmptyItem("Plain", EmptyShape.Plain);
                AddEmptyItem("Cube", EmptyShape.Cube);
                AddEmptyItem("Sphere", EmptyShape.Sphere);
                ImGui.EndMenu();
            }

            ImGui.Separator();

            bool hasSelection = _context.Selection.Selected.Count > 0;
            if (ImGui.MenuItem("Delete Selected", string.Empty, false, hasSelection))
            {
                DeleteSelected();
            }
        });
    }

    private void DeleteSelected()
    {
        EditSession session = _context.EditSessions.Active;
        foreach (SceneEntity entity in _context.Selection.Selected.OfType<SceneEntity>().ToList())
        {
            var command = new DeleteEntityCommand(_context.Scene, entity);
            command.Apply();
            session.Record(command);
            _context.Selection.Remove(entity);
        }
    }

    private void AddEmptyItem(string label, EmptyShape shape)
    {
        if (!ImGui.MenuItem(label))
        {
            return;
        }

        var empty = new EmptyEntity { Name = $"Empty ({shape})", Shape = shape };
        _context.Scene.Add(empty);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, empty));
    }
}
