using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using GVec3 = Godot.Vector3;
using NVec3 = System.Numerics.Vector3;

namespace WorldMapStudio;

/// <summary>
/// Default inspector for scene entities: edits world position and rotation across the whole
/// selection, showing "(multiple)" where targets differ and recording one undo command per edit.
/// </summary>
[Subsystem(nameof(InspectorWindow))]
public sealed class SceneEntityInspector : EntityInspector<SceneEntity>
{
    private const uint NameMaxLength = 128;

    private readonly EditorContext _editor;
    private Transform3D[]? _before;
    private readonly FieldEditTracker _entityTracker = new();
    private readonly ComponentFieldEditTracker _componentTracker = new();

    public SceneEntityInspector(InspectorWindow window)
    {
        _editor = window.Context;
    }

    protected override void DrawTargets(InspectorContext context, IReadOnlyList<SceneEntity> targets)
    {
        ImGui.Text(targets.Count == 1 ? targets[0].DisplayName : $"{targets.Count} entities selected");
        ImGui.Separator();

        if (targets.Count == 1)
        {
            DrawName(context, targets[0]);
            DrawGrouping(context, targets[0]);
        }

        DrawPosition(context, targets);
        DrawRotation(context, targets);

        if (targets.Count == 1)
        {
            ImGui.Separator();
            DrawSize(targets[0]);
            ImGui.Separator();
            DrawComponents(context, targets[0]);
        }
    }

    private void DrawName(InspectorContext context, SceneEntity target)
    {
        string name = target.Name;
        if (ImGui.InputText("Name", ref name, NameMaxLength))
        {
            target.Name = name;
        }

        _entityTracker.Track(context.Sessions, target, "name", target.Name, value => target.Name = value);
    }

    private void DrawGrouping(InspectorContext context, SceneEntity target)
    {
        string parentLabel = target.Parent?.DisplayName
            ?? (target.ParentRecordId is int id ? $"Unloaded parent #{id}" : "(none)");
        if (!ImGui.BeginCombo("Parent", parentLabel))
        {
            return;
        }

        if (ImGui.Selectable("(none)", target.Parent == null && target.ParentRecordId == null))
        {
            RecordParent(context, target, null);
        }

        foreach (SceneEntity candidate in _editor.Scene.Entities)
        {
            if (candidate is IDerivedEntity || !SetSceneEntityParentCommand.CanParentTo(target, candidate))
            {
                continue;
            }

            if (ImGui.Selectable($"{candidate.DisplayName}##{candidate.Id.Value}", ReferenceEquals(target.Parent, candidate)))
            {
                RecordParent(context, target, candidate);
            }
        }

        ImGui.EndCombo();
    }

    private void DrawPosition(InspectorContext context, IReadOnlyList<SceneEntity> targets)
    {
        bool uniform = AllEqual(targets, t => t.Transform.Origin, out GVec3 shared);
        NVec3 value = new(shared.X, shared.Y, shared.Z);
        if (ImGui.DragFloat3("Position", ref value, 0.05f))
        {
            var origin = new GVec3(value.X, value.Y, value.Z);
            foreach (SceneEntity target in targets)
            {
                Transform3D xform = target.Transform;
                xform.Origin = origin;
                target.Transform = xform;
            }
        }

        MarkMultiple(uniform);
        HandleEditLifecycle(context, targets);
    }

    private void DrawRotation(InspectorContext context, IReadOnlyList<SceneEntity> targets)
    {
        bool uniform = AllEqual(targets, t => t.Transform.Basis.GetEuler(), out GVec3 euler);
        NVec3 degrees = new(Mathf.RadToDeg(euler.X), Mathf.RadToDeg(euler.Y), Mathf.RadToDeg(euler.Z));
        if (ImGui.DragFloat3("Rotation", ref degrees, 0.5f))
        {
            var radians = new GVec3(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z));
            Basis basis = Basis.FromEuler(radians);
            foreach (SceneEntity target in targets)
            {
                target.Transform = new Transform3D(basis, target.Transform.Origin);
            }
        }

        MarkMultiple(uniform);
        HandleEditLifecycle(context, targets);
    }

    private static void DrawSize(SceneEntity target)
    {
        Vector3 size = target.LocalBounds.Size;
        ImGui.TextDisabled($"Effective size: {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##}");
    }

    private void DrawComponents(InspectorContext context, SceneEntity entity)
    {
        ImGui.Text("Components");
        DrawAddComponent(context, entity);

        foreach (SceneComponent component in entity.Components.ToList())
        {
            if (!ImGui.CollapsingHeader($"{component.DisplayName}##{component.TypeId}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            ImGui.PushID(component.TypeId);
            if (ImGui.SmallButton("Remove"))
            {
                var command = new RemoveComponentCommand(entity, component);
                command.Apply();
                context.Sessions.Record(command);
                ImGui.PopID();
                continue;
            }

            ImGui.Separator();
            switch (component)
            {
                case MarkerComponent marker:
                    DrawMarker(context, marker);
                    break;
                case StampComponent stamp:
                    DrawStamp(context, stamp);
                    break;
                case DrawingTargetComponent target:
                    DrawDrawingTarget(context, target);
                    break;
                case LandscapeMaterialBindComponent bind:
                    DrawLandscapeMaterialBind(context, bind);
                    break;
            }

            ImGui.PopID();
        }
    }

    private static void DrawAddComponent(InspectorContext context, SceneEntity entity)
    {
        if (!ImGui.BeginCombo("Add", "Choose component"))
        {
            return;
        }

        AddComponentItem(context, entity, "Marker", entity.Component<MarkerComponent>() == null, new MarkerComponent());
        AddComponentItem(context, entity, "Landscape Stamp", entity.Component<StampComponent>() == null, new StampComponent());
        AddComponentItem(context, entity, "Drawing Target", entity.Component<DrawingTargetComponent>() == null, new DrawingTargetComponent());
        AddComponentItem(context, entity, "Landscape Material Bind", entity.Component<LandscapeMaterialBindComponent>() == null, new LandscapeMaterialBindComponent());
        ImGui.EndCombo();
    }

    private static void AddComponentItem(
        InspectorContext context,
        SceneEntity entity,
        string label,
        bool enabled,
        SceneComponent component)
    {
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Selectable(label, false, enabled ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
        {
            SeedComponent(entity, component);
            var command = new AddComponentCommand(entity, component);
            command.Apply();
            context.Sessions.Record(command);
        }

        if (!enabled)
        {
            ImGui.EndDisabled();
        }
    }

    private static void SeedComponent(SceneEntity entity, SceneComponent component)
    {
        if (component is not StampComponent and not DrawingTargetComponent)
        {
            return;
        }

        // The menu seeds catalog bindings more thoroughly; inspector-added landscape components stay
        // intentionally blank because the inspector may be used before landscape/catalog data is loaded.
    }

    private void DrawMarker(InspectorContext context, MarkerComponent marker)
    {
        int shape = (int)marker.Shape;
        if (ImGui.Combo("Shape", ref shape, "Plain\0Cube\0Sphere\0"))
        {
            Record(context, marker, "shape", marker.Shape, (MarkerShape)shape, value => marker.Shape = value);
        }
    }

    private void DrawStamp(InspectorContext context, StampComponent stamp)
    {
        float radius = stamp.Radius;
        if (ImGui.DragFloat("Radius", ref radius, 0.5f, 0.5f, 4096.0f)) { stamp.Radius = radius; }
        _componentTracker.Track(context.Sessions, stamp, "radius", stamp.Radius, value => stamp.Radius = value);

        float falloff = stamp.Falloff;
        if (ImGui.DragFloat("Falloff", ref falloff, 0.01f, 0.0f, 1.0f)) { stamp.Falloff = falloff; }
        _componentTracker.Track(context.Sessions, stamp, "falloff", stamp.Falloff, value => stamp.Falloff = value);

        DrawLandscapeChannel(context, stamp, stamp.Channel, value => stamp.Channel = value);

        float strength = stamp.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { stamp.Strength = strength; }
        _componentTracker.Track(context.Sessions, stamp, "strength", stamp.Strength, value => stamp.Strength = value);

    }

    private void DrawDrawingTarget(InspectorContext context, DrawingTargetComponent target)
    {
        float sizeX = target.WorldSizeX;
        if (ImGui.DragFloat("Width", ref sizeX, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeX = sizeX; }
        _componentTracker.Track(context.Sessions, target, "width", target.WorldSizeX, value => target.WorldSizeX = value);

        float sizeZ = target.WorldSizeZ;
        if (ImGui.DragFloat("Depth", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeZ = sizeZ; }
        _componentTracker.Track(context.Sessions, target, "depth", target.WorldSizeZ, value => target.WorldSizeZ = value);

        DrawLandscapeChannel(context, target, target.Channel, value => target.Channel = value);

        float strength = target.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { target.Strength = strength; }
        _componentTracker.Track(context.Sessions, target, "strength", target.Strength, value => target.Strength = value);

        ImGui.TextDisabled($"{target.Width} x {target.Height} pixels");
        DrawResizeButton(context, target, 128);
        ImGui.SameLine();
        DrawResizeButton(context, target, 256);
        ImGui.SameLine();
        DrawResizeButton(context, target, 512);
        ImGui.SameLine();
        DrawClearButton(context, target);
    }

    private void DrawLandscapeMaterialBind(InspectorContext context, LandscapeMaterialBindComponent bind)
    {
        int priority = bind.Priority;
        if (ImGui.DragInt("Priority", ref priority)) { bind.Priority = priority; }
        _componentTracker.Track(context.Sessions, bind, "priority", bind.Priority, value => bind.Priority = value);

        LandscapeCatalog catalog = _editor.Landscape.Catalog;

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
                Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
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
            Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
        }
    }

    private void DrawLayerBinding(
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

    private void DrawMaterialBinding(
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
        Record(context, bind, "bindings", before, after, value => bind.ReplaceBindings(value));
    }

    private void DrawLandscapeChannel(
        InspectorContext context,
        SceneComponent component,
        string channel,
        Action<string> setChannel)
    {
        LandscapeCatalog catalog = _editor.Landscape.Catalog;

        if (ImGui.BeginCombo("Channel", channel.Length == 0 ? "(none)" : channel))
        {
            if (ImGui.Selectable("(none)", channel.Length == 0))
            {
                Record(context, component, "channel", channel, "", setChannel);
            }

            foreach (LandscapeChannel item in catalog.Channels)
            {
                if (ImGui.Selectable(item.Name, item.Name == channel))
                {
                    Record(context, component, "channel", channel, item.Name, setChannel);
                }
            }

            ImGui.EndCombo();
        }
    }

    private static void DrawResizeButton(InspectorContext context, DrawingTargetComponent target, int size)
    {
        bool current = target.Width == size && target.Height == size;
        if (current)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"{size}"))
        {
            int beforeWidth = target.Width;
            int beforeHeight = target.Height;
            byte[] beforePixels = target.CopyPixels();
            target.Resize(size, size);
            context.Sessions.Record(new ResizeDrawingTargetCommand(
                target,
                beforeWidth,
                beforeHeight,
                beforePixels,
                target.Width,
                target.Height,
                target.CopyPixels()));
        }

        if (current)
        {
            ImGui.EndDisabled();
        }
    }

    private static void DrawClearButton(InspectorContext context, DrawingTargetComponent target)
    {
        if (!ImGui.Button("Clear"))
        {
            return;
        }

        byte[] before = target.CopyPixels();
        target.ReplacePixels(new byte[target.Width * target.Height]);
        context.Sessions.Record(new SetDrawingTargetPixelsCommand(target, before, target.CopyPixels()));
    }

    private static void Record<T>(InspectorContext context, SceneComponent component, string field, T before, T after, Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetComponentFieldCommand<T>(component, field, set, before, after);
        command.Apply();
        context.Sessions.Record(command);
    }

    private static void RecordParent(InspectorContext context, SceneEntity target, SceneEntity? parent)
    {
        if (ReferenceEquals(target.Parent, parent) && target.ParentRecordId == parent?.RecordId)
        {
            return;
        }

        var command = new SetSceneEntityParentCommand(target, target.Parent, parent);
        command.Apply();
        context.Sessions.Record(command);
    }

    private static void MarkMultiple(bool uniform)
    {
        if (!uniform)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(multiple)");
        }
    }

    // Snapshots transforms when a field starts being edited and records a single command when it
    // finishes, so a whole drag over any number of targets is one undo step.
    private void HandleEditLifecycle(InspectorContext context, IReadOnlyList<SceneEntity> targets)
    {
        if (ImGui.IsItemActivated())
        {
            _before = new Transform3D[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                _before[i] = targets[i].Transform;
            }
        }

        if (!ImGui.IsItemDeactivatedAfterEdit() || _before == null || _before.Length != targets.Count)
        {
            return;
        }

        var entities = new SceneEntity[targets.Count];
        var after = new Transform3D[targets.Count];
        bool changed = false;
        for (int i = 0; i < targets.Count; i++)
        {
            entities[i] = targets[i];
            after[i] = targets[i].Transform;
            if (!_before[i].IsEqualApprox(after[i]))
            {
                changed = true;
            }
        }

        if (changed)
        {
            context.Sessions.Record(new TransformEntitiesCommand(entities, _before, after));
        }

        _before = null;
    }

    private static bool AllEqual(IReadOnlyList<SceneEntity> targets, Func<SceneEntity, GVec3> get, out GVec3 value)
    {
        value = get(targets[0]);
        for (int i = 1; i < targets.Count; i++)
        {
            if (!get(targets[i]).IsEqualApprox(value))
            {
                return false;
            }
        }

        return true;
    }
}
