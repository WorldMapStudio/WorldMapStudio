using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Hosts the top-level entries of the editor's main menu bar (File, Window, ...) as
/// <see cref="ISubsystem"/>s. Entries declare [Subsystem(nameof(MenuBarManager))] and are
/// constructed automatically by the generated InitializeSubsystems(), so a new top-level menu
/// (and everything it hosts) can register itself without touching <see cref="Editor"/>.
/// </summary>
[Subsystem(nameof(EditorContext))]
public sealed partial class MenuBarManager : ISubsystemHost, ISubsystem
{
    public Node3D Root { get; }

    /// <summary>The project being edited; carries the settings (like the axis convention) shared across the editor.</summary>
    public Project Project { get; }

    /// <summary>The coordinate system the user works in; every Godot-facing system routes through this.</summary>
    public AxisConvention Axes => Project.AxisConvention;

    /// <summary>Shared selection, forwarded from the context down to the windows.</summary>
    public SelectionSystem Selection { get; }

    /// <summary>Active edit session and undo history, forwarded from the context.</summary>
    public EditSessionManager EditSessions { get; }

    public float Priority => 0f;

    public MenuBarManager(EditorContext context)
    {
        Root = context.Root;
        Project = context.Project;
        Selection = context.Selection;
        EditSessions = context.EditSessions;
        InitializeSubsystems();
    }

    public void Draw()
    {
        foreach (IMainMenu menu in Subsystems.Cast<IMainMenu>())
        {
            menu.Draw();
        }
    }
}
