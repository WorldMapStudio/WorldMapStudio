using System.Collections.Generic;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// "Name it" popup for saving scene entities as a prefab (small form popup, modelled on
/// <see cref="ProceduralModelPicker"/>'s create popup, minus the id/function fields this doesn't
/// need). Owned and drawn by <see cref="SceneEntityInspector"/>.
/// </summary>
public sealed class SavePrefabPopup
{
    private const string PopupId = "Save as Prefab";

    private readonly PrefabSystem _prefabs;

    private bool _openRequested;
    private bool _active;
    private IReadOnlyList<SceneEntity> _sources = [];
    private string _name = "Prefab";

    public SavePrefabPopup(PrefabSystem prefabs)
    {
        _prefabs = prefabs;
    }

    public void Open(IReadOnlyList<SceneEntity> sources)
    {
        _sources = sources;
        _name = sources[0].Name;
        _openRequested = true;
    }

    public void Draw()
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
            _active = true;
        }

        if (!_active)
        {
            return;
        }

        bool open = true;
        ImGuiEx.PopupModal(PopupId, true, ref open, ImGuiWindowFlags.AlwaysAutoResize, () =>
        {
            ImGui.InputText("Name", ref _name, 128);

            if (ImGui.Button("Save", new Vector2(120, 0)))
            {
                Commit();
                open = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                open = false;
            }
        });

        if (!open)
        {
            _active = false;
            _sources = [];
        }
    }

    private void Commit()
    {
        if (_sources.Count > 0)
        {
            _prefabs.Save(_sources, _name);
        }
    }
}
