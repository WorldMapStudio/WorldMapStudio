using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Every registered <see cref="IMapPropertiesSection"/>. A plain member of <see cref="EditorContext"/>
/// (the core spine, not an extension point itself), but itself a host so plugins register their own
/// per-map settings into it, exactly like <see cref="SceneComponentRegistry"/> hosts component kinds.
/// </summary>
[SubsystemHost(typeof(IMapPropertiesSection))]
public sealed partial class MapPropertiesRegistry : ISubsystemHost
{
    public MapPropertiesRegistry(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    public EditorContext Context { get; }

    public IEnumerable<IMapPropertiesSection> All => Subsystems;
}
