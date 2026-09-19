using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Every registered <see cref="ISceneComponentType"/>. A plain member of <see cref="EditorContext"/>
/// (the core spine, not an extension point), but itself a host so plugins register their own
/// component kinds into it, exactly like <see cref="LandscapeSystem"/> hosts landscape profiles.
/// </summary>
[SubsystemHost(typeof(ISceneComponentType))]
public sealed partial class SceneComponentRegistry : ISubsystemHost
{
    public SceneComponentRegistry(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    public EditorContext Context { get; }

    public IEnumerable<ISceneComponentType> All => Subsystems;

    public ISceneComponentType? Find(string typeId) => All.FirstOrDefault(type => type.TypeId == typeId);
}
