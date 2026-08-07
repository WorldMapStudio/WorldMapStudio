using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Lists the loaded scene entities and mirrors the shared selection: clicking an entry selects it
/// (Ctrl/Shift to add or remove), and entities selected in the viewport show as highlighted here.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class OutlineWindow : Window
{
    private readonly SceneEntityRegistry _scene;
    private readonly SelectionSystem _selection;

    public OutlineWindow(WindowManager manager)
        : base("Outline", defaultSize: new Vector2(240, 400))
    {
        _scene = manager.Scene;
        _selection = manager.Selection;
    }

    protected override void DrawContent()
    {
        IReadOnlyList<SceneEntity> entities = _scene.Entities;
        if (entities.Count == 0)
        {
            ImGui.TextDisabled("No entities loaded.");
            return;
        }

        foreach (SceneEntity entity in entities)
        {
            bool selected = _selection.IsSelected(entity);
            if (ImGui.Selectable($"{entity.DisplayName}##{entity.Id.Value}", selected))
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
        }
    }
}
