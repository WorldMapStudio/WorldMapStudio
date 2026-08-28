using System;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>Shared inspector helper for landscape deformer components (stamp, drawing target):
/// the "which channel does this write to" combo.</summary>
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
}
