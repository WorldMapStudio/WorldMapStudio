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
    private readonly SavePrefabPopup _savePrefabPopup;

    public SceneEntityInspector(InspectorWindow window)
    {
        _editor = window.Context;
        _savePrefabPopup = new SavePrefabPopup(_editor.Prefabs, _editor.EditSessions);
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
        DrawScale(context, targets);

        if (targets.Count == 1)
        {
            ImGui.Separator();
            DrawSize(targets[0]);
            ImGui.Separator();
            DrawComponents(context, targets[0]);
            ImGui.Separator();
            if (ImGui.Button("Save as Prefab"))
            {
                _savePrefabPopup.Open(targets[0]);
            }
        }

        _savePrefabPopup.Draw();
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
        bool uniform = AllEqual(targets, t => t.Transform.Basis.Orthonormalized().GetEuler(), out GVec3 euler);
        NVec3 degrees = new(Mathf.RadToDeg(euler.X), Mathf.RadToDeg(euler.Y), Mathf.RadToDeg(euler.Z));
        if (ImGui.DragFloat3("Rotation", ref degrees, 0.5f))
        {
            var radians = new GVec3(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z));
            Basis rotation = Basis.FromEuler(radians);
            foreach (SceneEntity target in targets)
            {
                Transform3D xform = target.Transform;
                xform.Basis = rotation.ScaledLocal(xform.Basis.Scale);
                target.Transform = xform;
            }
        }

        MarkMultiple(uniform);
        HandleEditLifecycle(context, targets);
    }

    private void DrawScale(InspectorContext context, IReadOnlyList<SceneEntity> targets)
    {
        SelfScale mode = targets[0].SelfScale;
        for (int i = 1; i < targets.Count; i++)
        {
            if (targets[i].SelfScale < mode)
            {
                mode = targets[i].SelfScale;
            }
        }

        if (mode == SelfScale.None)
        {
            return;
        }

        bool uniform = AllEqual(targets, t => t.Transform.Basis.Scale, out GVec3 shared);
        bool changed;
        GVec3 scale;
        if (mode == SelfScale.Uniform)
        {
            float value = shared.X;
            changed = ImGui.DragFloat("Scale", ref value, 0.02f);
            scale = GVec3.One * value;
        }
        else
        {
            NVec3 value = new(shared.X, shared.Y, shared.Z);
            changed = ImGui.DragFloat3("Scale", ref value, 0.02f);
            scale = new GVec3(value.X, value.Y, value.Z);
        }

        if (changed)
        {
            foreach (SceneEntity target in targets)
            {
                Transform3D xform = target.Transform;
                xform.Basis = xform.Basis.Orthonormalized().ScaledLocal(scale);
                target.Transform = xform;
            }
        }

        MarkMultiple(uniform);
        HandleEditLifecycle(context, targets);
    }

    private static void DrawSize(SceneEntity target)
    {
        Vector3 size = target.LocalBounds.Size * target.Transform.Basis.Scale;
        ImGui.TextDisabled($"Effective size: {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##}");
    }

    private void DrawComponents(InspectorContext context, SceneEntity entity)
    {
        ImGui.Text("Components");
        DrawAddComponent(context, entity);

        foreach (SceneComponent component in entity.Components.ToList())
        {
            ISceneComponentType? type = _editor.ComponentTypes.Find(component.TypeId);
            string label = type?.DisplayName ?? component.DisplayName;
            if (!ImGui.CollapsingHeader($"{label}##{component.TypeId}", ImGuiTreeNodeFlags.DefaultOpen))
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
            type?.DrawInspector(context, component);
            ImGui.PopID();
        }
    }

    private void DrawAddComponent(InspectorContext context, SceneEntity entity)
    {
        if (!ImGui.BeginCombo("Add", "Choose component"))
        {
            return;
        }

        foreach (ISceneComponentType type in _editor.ComponentTypes.All)
        {
            AddComponentItem(context, entity, type);
        }

        ImGui.EndCombo();
    }

    private static void AddComponentItem(InspectorContext context, SceneEntity entity, ISceneComponentType type)
    {
        bool enabled = type.CanAddTo(entity);
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Selectable(type.DisplayName, false, enabled ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
        {
            var command = new AddComponentCommand(entity, type.Create());
            command.Apply();
            context.Sessions.Record(command);
        }

        if (!enabled)
        {
            ImGui.EndDisabled();
        }
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
