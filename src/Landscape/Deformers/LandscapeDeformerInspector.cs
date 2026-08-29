using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Shared inspector helper for landscape deformer components (stamp, drawing target):
/// the "which channel does this write to" combo, and the multi-select checklist for deformers
/// (like <see cref="TerrainValueComponent"/>) that write onto several channels at once.</summary>
internal static class LandscapeDeformerInspector
{
    public static void DrawChannelCombo(
        InspectorContext context,
        LandscapeCatalog catalog,
        SceneComponent component,
        string channel,
        Action<string> setChannel)
    {
        if (!ImGui.BeginCombo("Channel", channel.Length == 0 ? "(none)" : channel))
        {
            return;
        }

        if (ImGui.Selectable("(none)", channel.Length == 0))
        {
            ComponentFieldRecorder.Record(context, component, "channel", channel, "", setChannel);
        }

        foreach (LandscapeChannel item in catalog.Channels)
        {
            if (ImGui.Selectable(item.Name, item.Name == channel))
            {
                ComponentFieldRecorder.Record(context, component, "channel", channel, item.Name, setChannel);
            }
        }

        ImGui.EndCombo();
    }

    public static void DrawChannelChecklist(
        InspectorContext context,
        LandscapeCatalog catalog,
        SceneComponent component,
        IReadOnlyList<string> channels,
        Action<IEnumerable<string>> setChannels)
    {
        ImGui.TextDisabled("Channels");

        foreach (LandscapeChannel item in catalog.Channels)
        {
            bool selected = channels.Contains(item.Name);
            bool next = selected;
            if (!ImGui.Checkbox(item.Name, ref next) || next == selected)
            {
                continue;
            }

            List<string> updated = channels.ToList();
            if (next)
            {
                updated.Add(item.Name);
            }
            else
            {
                updated.Remove(item.Name);
            }

            ComponentFieldRecorder.Record(context, component, "channels", channels, (IReadOnlyList<string>)updated, setChannels);
        }
    }
}
