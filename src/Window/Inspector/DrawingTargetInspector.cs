using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Edits a landscape drawing target and its channel/layer/material bindings.</summary>
[Subsystem(nameof(InspectorWindow))]
public sealed class DrawingTargetInspector : EntityInspector<DrawingTargetEntity>
{
    private const uint NameMaxLength = 128;

    private readonly EditorContext _editor;
    private readonly FieldEditTracker _tracker = new();

    public DrawingTargetInspector(InspectorWindow window)
    {
        _editor = window.Context;
    }

    public override float Priority => -1.0f;

    protected override void DrawTargets(InspectorContext context, IReadOnlyList<DrawingTargetEntity> targets)
    {
        if (targets.Count != 1)
        {
            ImGui.Text($"{targets.Count} drawing targets selected");
            ImGui.TextDisabled("Select one to edit its landscape bindings.");
            return;
        }

        DrawingTargetEntity target = targets[0];
        LandscapeCatalog catalog = _editor.Landscape.Catalog;

        string name = target.Name;
        if (ImGui.InputText("Name", ref name, NameMaxLength)) { target.Name = name; }
        _tracker.Track(context.Sessions, target, "name", target.Name, value => target.Name = value);

        ImGui.Separator();

        float sizeX = target.WorldSizeX;
        if (ImGui.DragFloat("Width", ref sizeX, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeX = sizeX; }
        _tracker.Track(context.Sessions, target, "width", target.WorldSizeX, value => target.WorldSizeX = value);

        float sizeZ = target.WorldSizeZ;
        if (ImGui.DragFloat("Depth", ref sizeZ, 0.5f, 0.5f, 4096.0f)) { target.WorldSizeZ = sizeZ; }
        _tracker.Track(context.Sessions, target, "depth", target.WorldSizeZ, value => target.WorldSizeZ = value);

        float strength = target.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { target.Strength = strength; }
        _tracker.Track(context.Sessions, target, "strength", target.Strength, value => target.Strength = value);

        int priority = target.Priority;
        if (ImGui.DragInt("Priority", ref priority)) { target.Priority = priority; }
        _tracker.Track(context.Sessions, target, "priority", target.Priority, value => target.Priority = value);

        ImGui.TextDisabled($"{target.Width} x {target.Height} pixels");
        DrawResizeButton(context, target, 128);
        ImGui.SameLine();
        DrawResizeButton(context, target, 256);
        ImGui.SameLine();
        DrawResizeButton(context, target, 512);
        ImGui.SameLine();
        DrawClearButton(context, target);

        ImGui.Separator();

        DrawChannel(context, target, catalog);
        DrawLayer(context, target, catalog);
        DrawMaterial(context, target, catalog);
    }

    private static void DrawResizeButton(InspectorContext context, DrawingTargetEntity target, int size)
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

    private static void DrawClearButton(InspectorContext context, DrawingTargetEntity target)
    {
        if (!ImGui.Button("Clear"))
        {
            return;
        }

        byte[] before = target.CopyPixels();
        target.ReplacePixels(new byte[target.Width * target.Height]);
        context.Sessions.Record(new SetDrawingTargetPixelsCommand(target, before, target.CopyPixels()));
    }

    private void DrawChannel(InspectorContext context, DrawingTargetEntity target, LandscapeCatalog catalog)
    {
        if (ImGui.BeginCombo("Channel", target.Channel.Length == 0 ? "(none)" : target.Channel))
        {
            if (ImGui.Selectable("(none)", target.Channel.Length == 0))
            {
                Record(context, target, "channel", target.Channel, "", value => target.Channel = value);
            }

            foreach (LandscapeChannel channel in catalog.Channels)
            {
                if (ImGui.Selectable(channel.Name, channel.Name == target.Channel))
                {
                    Record(context, target, "channel", target.Channel, channel.Name, value => target.Channel = value);
                }
            }

            ImGui.EndCombo();
        }

        if (target.Channel.Length > 0 && catalog.Channels.All(channel => channel.Name != target.Channel))
        {
            ImGui.TextDisabled($"Channel '{target.Channel}' no longer exists.");
        }
    }

    private void DrawLayer(InspectorContext context, DrawingTargetEntity target, LandscapeCatalog catalog)
    {
        int? current = target.LayerId;
        LandscapeLayer? bound = current is { } boundId
            ? catalog.Layers.FirstOrDefault(layer => layer.RecordId == boundId)
            : null;

        if (ImGui.BeginCombo("Layer", bound?.Name ?? "(none)"))
        {
            if (ImGui.Selectable("(none)", current == null))
            {
                Record(context, target, "layer", current, null, value => target.LayerId = value);
            }

            foreach (LandscapeLayer layer in catalog.LayersInOrder)
            {
                int? recordId = layer.RecordId;
                if (ImGui.Selectable($"{layer.Name}##{recordId}", recordId == current))
                {
                    Record(context, target, "layer", current, recordId, value => target.LayerId = value);
                }
            }

            ImGui.EndCombo();
        }

        if (current != null && bound == null)
        {
            ImGui.TextDisabled("Bound layer is not loaded.");
        }
    }

    private void DrawMaterial(InspectorContext context, DrawingTargetEntity target, LandscapeCatalog catalog)
    {
        LandscapeMaterial? bound = target.MaterialId is { } boundId
            ? catalog.Materials.FirstOrDefault(m => m.RecordId == boundId)
            : null;

        if (ImGui.BeginCombo("Material", bound?.Name ?? "(none)"))
        {
            if (ImGui.Selectable("(none)", target.MaterialId == null))
            {
                Record(context, target, "material", target.MaterialId, null, value => target.MaterialId = value);
            }

            foreach (LandscapeMaterial material in catalog.Materials)
            {
                int? recordId = material.RecordId;
                if (ImGui.Selectable($"{material.Name}##{recordId}", recordId == target.MaterialId))
                {
                    Record(context, target, "material", target.MaterialId, recordId, value => target.MaterialId = value);
                }
            }

            ImGui.EndCombo();
        }
    }

    private static void Record<T>(
        InspectorContext context,
        DrawingTargetEntity target,
        string field,
        T before,
        T after,
        System.Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetFieldCommand<T>(target, field, set, before, after);
        command.Apply();
        context.Sessions.Record(command);
    }
}
