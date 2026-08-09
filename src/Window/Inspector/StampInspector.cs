using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Edits a landscape stamp: its shape, and which catalog layers, material and channel it drives.
/// Layers and materials are project data rather than source code, so they are picked here as instance
/// parameters rather than hardcoded by the entity.
/// </summary>
[Subsystem(nameof(InspectorWindow))]
public sealed class StampInspector : EntityInspector<StampEntity>
{
    private const uint NameMaxLength = 128;

    private readonly EditorContext _editor;
    private readonly FieldEditTracker _tracker = new();

    public StampInspector(InspectorWindow window)
    {
        _editor = window.Context;
    }

    public override float Priority => -1.0f; // more specific than the generic scene entity inspector

    protected override void DrawTargets(InspectorContext context, IReadOnlyList<StampEntity> targets)
    {
        if (targets.Count != 1)
        {
            ImGui.Text($"{targets.Count} stamps selected");
            ImGui.TextDisabled("Select one to edit its landscape bindings.");
            return;
        }

        StampEntity stamp = targets[0];
        LandscapeCatalog catalog = _editor.Landscape.Catalog;

        string name = stamp.Name;
        if (ImGui.InputText("Name", ref name, NameMaxLength)) { stamp.Name = name; }
        _tracker.Track(context.Sessions, stamp, "name", stamp.Name, value => stamp.Name = value);

        ImGui.Separator();

        float radius = stamp.Radius;
        if (ImGui.DragFloat("Radius", ref radius, 0.5f, 0.5f, 4096.0f)) { stamp.Radius = radius; }
        _tracker.Track(context.Sessions, stamp, "radius", stamp.Radius, value => stamp.Radius = value);

        float falloff = stamp.Falloff;
        if (ImGui.DragFloat("Falloff", ref falloff, 0.01f, 0.0f, 1.0f)) { stamp.Falloff = falloff; }
        _tracker.Track(context.Sessions, stamp, "falloff", stamp.Falloff, value => stamp.Falloff = value);

        float strength = stamp.Strength;
        if (ImGui.DragFloat("Strength", ref strength, 0.01f, 0.0f, 1.0f)) { stamp.Strength = strength; }
        _tracker.Track(context.Sessions, stamp, "strength", stamp.Strength, value => stamp.Strength = value);

        int priority = stamp.Priority;
        if (ImGui.DragInt("Priority", ref priority)) { stamp.Priority = priority; }
        _tracker.Track(context.Sessions, stamp, "priority", stamp.Priority, value => stamp.Priority = value);

        ImGui.Separator();

        DrawChannel(context, stamp, catalog);
        DrawLayer(context, stamp, catalog, "Texture layer", stamp.TextureLayerId,
            layer => layer.UsesTextureSlot, value => stamp.TextureLayerId = value);
        DrawLayer(context, stamp, catalog, "Height layer", stamp.HeightLayerId,
            layer => !layer.UsesTextureSlot, value => stamp.HeightLayerId = value);
        DrawMaterial(context, stamp, catalog);
    }

    private void DrawChannel(InspectorContext context, StampEntity stamp, LandscapeCatalog catalog)
    {
        if (ImGui.BeginCombo("Channel", stamp.Channel.Length == 0 ? "(none)" : stamp.Channel))
        {
            foreach (LandscapeChannel channel in catalog.Channels)
            {
                if (ImGui.Selectable(channel.Name, channel.Name == stamp.Channel))
                {
                    Record(context, stamp, "channel", stamp.Channel, channel.Name, value => stamp.Channel = value);
                }
            }

            ImGui.EndCombo();
        }

        if (stamp.Channel.Length > 0 && catalog.Channels.All(channel => channel.Name != stamp.Channel))
        {
            ImGui.TextDisabled($"Channel '{stamp.Channel}' no longer exists.");
        }
    }

    private void DrawLayer(
        InspectorContext context,
        StampEntity stamp,
        LandscapeCatalog catalog,
        string label,
        int? current,
        System.Func<LandscapeLayer, bool> filter,
        System.Action<int?> set)
    {
        LandscapeLayer? bound = catalog.Layers.FirstOrDefault(layer => layer.RecordId == current);

        if (ImGui.BeginCombo(label, bound?.Name ?? "(none)"))
        {
            if (ImGui.Selectable("(none)", current == null))
            {
                Record(context, stamp, label, current, null, set);
            }

            foreach (LandscapeLayer layer in catalog.Layers.Where(filter).Where(layer => layer.RecordId != null))
            {
                if (ImGui.Selectable($"{layer.Name}##{layer.RecordId}", layer.RecordId == current))
                {
                    Record(context, stamp, label, current, layer.RecordId, set);
                }
            }

            ImGui.EndCombo();
        }

        if (current != null && bound == null)
        {
            // Layers are only pickable once committed, so an uncommitted layer reads as missing here.
            ImGui.TextDisabled("Bound layer is not loaded.");
        }
    }

    private void DrawMaterial(InspectorContext context, StampEntity stamp, LandscapeCatalog catalog)
    {
        LandscapeTextureMaterial? bound = catalog.Materials.FirstOrDefault(m => m.RecordId == stamp.MaterialId);

        if (ImGui.BeginCombo("Material", bound?.Name ?? "(none)"))
        {
            if (ImGui.Selectable("(none)", stamp.MaterialId == null))
            {
                Record(context, stamp, "material", stamp.MaterialId, null, value => stamp.MaterialId = value);
            }

            foreach (LandscapeTextureMaterial material in catalog.Materials.Where(m => m.RecordId != null))
            {
                if (ImGui.Selectable($"{material.Name}##{material.RecordId}", material.RecordId == stamp.MaterialId))
                {
                    Record(context, stamp, "material", stamp.MaterialId, material.RecordId,
                        value => stamp.MaterialId = value);
                }
            }

            ImGui.EndCombo();
        }
    }

    // A combo has no activate/deactivate pair to bracket, so its edit is recorded on the spot.
    private static void Record<T>(
        InspectorContext context,
        StampEntity stamp,
        string field,
        T before,
        T after,
        System.Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetFieldCommand<T>(stamp, field, set, before, after);
        command.Apply();
        context.Sessions.Record(command);
    }
}
