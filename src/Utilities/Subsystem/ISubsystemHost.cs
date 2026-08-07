namespace WorldMapStudio;

using System.Collections.Generic;
using System.Linq;

public interface ISubsystemHost
{
    public void InitializeSubsystems() {}

    public IEnumerable<ISubsystem> Subsystems => Enumerable.Empty<ISubsystem>();
}
