using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's data backends. Storages self-register with [Subsystem(nameof(DatabaseSystem))]
/// and are constructed by the generated InitializeSubsystems(), so the built-in "Editor" storage and
/// any plugin storages register without touching this class. A plain member of
/// <see cref="EditorContext"/> (it is the core spine, not an extension point), but itself a host.
/// </summary>
public sealed partial class DatabaseSystem : ISubsystemHost
{
    public IEnumerable<Storage> Storages => Subsystems.Cast<Storage>();

    public DatabaseSystem()
    {
        InitializeSubsystems();
    }
}
