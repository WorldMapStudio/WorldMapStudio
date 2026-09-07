using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The strip of controls across the top of the <see cref="ViewportWindow"/>, above the active
/// tool's toolbar. Hosts <see cref="IViewportHeaderItem"/>s as <see cref="ISubsystem"/>s, so
/// anything — including a plugin — can add a control here with [Subsystem(nameof(ViewportHeader))]
/// and never touch the viewport itself. Draws nothing when no item is registered.
/// </summary>
public sealed partial class ViewportHeader : ISubsystemHost
{
    /// <summary>The editor's shared systems, forwarded to hosted items.</summary>
    public EditorContext Context { get; }

    public ViewportHeader(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    public void Draw(in ViewportHeaderContext context)
    {
        bool any = false;
        foreach (IViewportHeaderItem item in Subsystems.Cast<IViewportHeaderItem>())
        {
            if (any)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
            }

            ImGui.PushID(item.GetType().FullName);
            item.Draw(context);
            ImGui.PopID();
            any = true;
        }

        if (any)
        {
            ImGui.Separator();
        }
    }
}
