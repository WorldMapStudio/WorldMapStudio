using System.Linq;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The registered extras on the <see cref="ViewportWindow"/>'s toolbar row, drawn inline after the
/// active tool's own controls. Hosts <see cref="IViewportHeaderItem"/>s as <see cref="ISubsystem"/>s,
/// so anything — including a plugin — can add a control to that row with
/// [Subsystem(nameof(ViewportHeader))] and never touch the viewport itself. Draws nothing when no
/// item is registered.
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

    /// <param name="continueRow">True when the active tool already drew controls on this row, so the
    /// first item is placed after them rather than at the start of the line.</param>
    public void Draw(in ViewportHeaderContext context, bool continueRow)
    {
        bool started = continueRow;
        foreach (IViewportHeaderItem item in Subsystems.Cast<IViewportHeaderItem>())
        {
            if (started)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
            }

            ImGui.PushID(item.GetType().FullName);
            item.Draw(context);
            ImGui.PopID();
            started = true;
        }
    }
}
