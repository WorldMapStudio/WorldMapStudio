using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

public interface ISubsystemHost
{
    public void InitializeSubsystems() {}

    public IEnumerable<ISubsystem> Subsystems => Enumerable.Empty<ISubsystem>();
}
