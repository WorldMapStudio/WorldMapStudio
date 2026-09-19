using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

public sealed class PrefabSelectionDialog : IModalDialog<PrefabSelectionContext>
{
    private static readonly Vector2 BodySize = new(420, 320);

    private string _filter = "";
    private Prefab? _highlighted;

    public ModalDialogState Draw(PrefabSelectionContext context)
    {
        ImGui.Text("Select Prefab");
        ImGui.Separator();

        ImGui.SetNextItemWidth(BodySize.X);
        ImGui.InputTextWithHint("##filter", "Filter prefabs...", ref _filter, 128);

        List<Prefab> filtered = Filter(context.Prefabs, _filter);

        ImGui.BeginChild("PrefabList", BodySize, true, ImGuiWindowFlags.None);
        if (filtered.Count == 0)
        {
            ImGui.TextDisabled(context.Prefabs.Count == 0 ? "No prefabs saved yet." : "No prefabs match the filter.");
        }
        else
        {
            foreach (Prefab prefab in filtered)
            {
                bool selected = ReferenceEquals(prefab, _highlighted);
                if (ImGui.Selectable($"{prefab.Name}##{prefab.RecordId}", selected))
                {
                    _highlighted = prefab;
                }
            }
        }

        ImGui.EndChild();
        ImGui.Separator();

        bool canAct = _highlighted != null;
        bool spawn = false;
        bool delete = false;

        if (!canAct)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Spawn", new Vector2(120, 0)))
        {
            spawn = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete", new Vector2(120, 0)))
        {
            delete = true;
        }

        if (!canAct)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            return ModalDialogState.Cancelled;
        }

        if (spawn)
        {
            context.Select(_highlighted!);
            return ModalDialogState.Confirmed;
        }

        if (delete)
        {
            context.Delete(_highlighted!);
            _highlighted = null;
        }

        return ModalDialogState.Running;
    }

    private static List<Prefab> Filter(IReadOnlyList<Prefab> prefabs, string filter)
    {
        string trimmed = filter.Trim();
        IEnumerable<Prefab> query = trimmed.Length == 0
            ? prefabs
            : prefabs.Where(prefab => prefab.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        return query.OrderBy(prefab => prefab.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
