using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(SceneComponentRegistry))]
public sealed class LandscapeMaterialBindComponentType : ISceneComponentType
{
    private readonly LandscapeSystem _landscape;
    private readonly ComponentFieldEditTracker _tracker = new();

    public float Priority => 3.0f;

    public LandscapeMaterialBindComponentType(SceneComponentRegistry registry)
    {
        _landscape = registry.Context.Landscape;
    }

    public string TypeId => LandscapeMaterialBindComponent.Kind;

    public string DisplayName => "Landscape Material Bind";

    public SceneComponent Create() => new LandscapeMaterialBindComponent();

    public void DrawInspector(InspectorContext context, SceneComponent component)
    {
        var bind = (LandscapeMaterialBindComponent)component;

        int priority = bind.Priority;
        if (ImGui.DragInt("Priority", ref priority)) { bind.Priority = priority; }
        _tracker.Track(context.Sessions, bind, "priority", bind.Priority, value => bind.Priority = value);

        LandscapeCatalog catalog = _landscape.Catalog;

        for (int i = 0; i < bind.Bindings.Count; i++)
        {
            LandscapeMaterialBinding binding = bind.Bindings[i];
            ImGui.PushID(i);
            ImGui.Separator();

            DrawLayerBinding(context, bind, catalog, i, binding);
            DrawMaterialBinding(context, bind, catalog, i, binding);

            if (ImGui.SmallButton("Remove binding"))
            {
                List<LandscapeMaterialBinding> before = bind.Bindings.ToList();
                List<LandscapeMaterialBinding> after = bind.Bindings.ToList();
                after.RemoveAt(i);
                ComponentFieldRecorder.Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
                ImGui.PopID();
                return;
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add binding"))
        {
            List<LandscapeMaterialBinding> before = bind.Bindings.ToList();
            List<LandscapeMaterialBinding> after = bind.Bindings.ToList();
            after.Add(new LandscapeMaterialBinding(
                catalog.Layers.FirstOrDefault(layer => !layer.IsBase)?.RecordId ?? catalog.Layers.FirstOrDefault()?.RecordId,
                catalog.Materials.FirstOrDefault()?.RecordId));
            ComponentFieldRecorder.Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
        }
    }

    private static void DrawLayerBinding(
        InspectorContext context,
        LandscapeMaterialBindComponent bind,
        LandscapeCatalog catalog,
        int index,
        LandscapeMaterialBinding binding)
    {
        LandscapeLayer? boundLayer = binding.LayerId is { } lid
            ? catalog.Layers.FirstOrDefault(layer => layer.RecordId == lid)
            : null;
        if (!ImGui.BeginCombo("Layer", boundLayer?.Name ?? "(none)"))
        {
            return;
        }

        if (ImGui.Selectable("(none)", binding.LayerId == null))
        {
            ReplaceBinding(context, bind, index, binding with { LayerId = null });
        }

        foreach (LandscapeLayer layer in catalog.LayersInOrder)
        {
            int? recordId = layer.RecordId;
            if (ImGui.Selectable($"{layer.Name}##{recordId}", recordId == binding.LayerId))
            {
                ReplaceBinding(context, bind, index, binding with { LayerId = recordId });
            }
        }

        ImGui.EndCombo();
    }

    private static void DrawMaterialBinding(
        InspectorContext context,
        LandscapeMaterialBindComponent bind,
        LandscapeCatalog catalog,
        int index,
        LandscapeMaterialBinding binding)
    {
        LandscapeMaterial? boundMaterial = binding.MaterialId is { } mid
            ? catalog.Materials.FirstOrDefault(material => material.RecordId == mid)
            : null;
        if (!ImGui.BeginCombo("Material", boundMaterial?.Name ?? "(none)"))
        {
            return;
        }

        if (ImGui.Selectable("(none)", binding.MaterialId == null))
        {
            ReplaceBinding(context, bind, index, binding with { MaterialId = null });
        }

        foreach (LandscapeMaterial material in catalog.Materials)
        {
            int? recordId = material.RecordId;
            if (ImGui.Selectable($"{material.Name}##{recordId}", recordId == binding.MaterialId))
            {
                ReplaceBinding(context, bind, index, binding with { MaterialId = recordId });
            }
        }

        ImGui.EndCombo();
    }

    private static void ReplaceBinding(
        InspectorContext context,
        LandscapeMaterialBindComponent bind,
        int index,
        LandscapeMaterialBinding replacement)
    {
        List<LandscapeMaterialBinding> before = bind.Bindings.ToList();
        List<LandscapeMaterialBinding> after = bind.Bindings.ToList();
        after[index] = replacement;
        ComponentFieldRecorder.Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
    }
}
