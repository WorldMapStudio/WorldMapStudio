using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's "File" menu entries as <see cref="ISubsystem"/>s. Items declare
/// [Subsystem(nameof(FileMenuManager))] and are constructed automatically by the generated
/// InitializeSubsystems(), letting features like "Exit" register themselves instead of being
/// wired up by hand in <see cref="Editor"/>.
/// </summary>
public sealed partial class FileMenuManager : ISubsystemHost
{
    public bool ExitRequested { get; private set; }

    public FileMenuManager()
    {
        InitializeSubsystems();
    }

    public void RequestExit() => ExitRequested = true;

    public void Draw()
    {
        IEnumerable<IFileMenuItem> items = Subsystems.Cast<IFileMenuItem>();
        ImGuiEx.Menu("File", () =>
        {
            foreach (IFileMenuItem item in items)
            {
                item.Draw();
            }
        });
    }
}
