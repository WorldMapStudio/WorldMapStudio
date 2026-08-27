using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class ExportWindow : Window
{
    private readonly ExportSystem _exports;
    private int _selected;
    private ChunkExportScope _scope;

    public ExportWindow(WindowManager manager)
        : base("Export", startOpen: false, defaultSize: new NVector2(480.0f, 360.0f))
    {
        _exports = manager.Context.Exports;
    }

    protected override void DrawContent()
    {
        List<IChunkExportScript> exporters = _exports.Exporters.ToList();
        if (exporters.Count == 0)
        {
            ImGui.TextDisabled("No exporters registered.");
            return;
        }

        _selected = System.Math.Clamp(_selected, 0, exporters.Count - 1);
        string preview = exporters[_selected].DisplayName;
        if (ImGui.BeginCombo("Exporter", preview))
        {
            for (int i = 0; i < exporters.Count; i++)
            {
                bool selected = i == _selected;
                if (ImGui.Selectable(exporters[i].DisplayName, selected))
                {
                    _selected = i;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        int scope = (int)_scope;
        if (ImGui.Combo("Scope", ref scope, "Current map\0All maps\0"))
        {
            _scope = (ChunkExportScope)scope;
        }

        IChunkExportScript exporter = exporters[_selected];
        int dirty = _exports.Changes.DirtyFor(exporter.Id, _scope).Count;
        ImGui.TextDisabled($"{dirty} dirty chunks");

        ImGui.Separator();
        exporter.DrawSettings();
        ImGui.Separator();

        ImGui.BeginDisabled(dirty == 0);
        if (ImGui.Button("Export Dirty", new NVector2(130.0f, 0.0f)))
        {
            _exports.Run(exporter, _scope);
        }
        ImGui.EndDisabled();
    }
}
