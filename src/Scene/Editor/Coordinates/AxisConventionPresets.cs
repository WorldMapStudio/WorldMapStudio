using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the axis convention presets offered when a project's coordinate system is set up. A subsystem
/// of <see cref="AppSystems"/> rather than of a project, since <see cref="CreateProjectOperation"/>
/// needs the list before a <see cref="Project"/>, let alone an <c>EditorContext</c>, exists. Landscape
/// settings solve the same "plugins contribute presets" problem with <see cref="ILandscapeProfile"/>
/// hosted on the (project-scoped) <c>LandscapeSystem</c>; this is that pattern's counterpart for the
/// one setting that has to exist before a project does.
/// </summary>
[Subsystem(nameof(AppSystems))]
public sealed partial class AxisConventionPresets : ISubsystemHost, ISubsystem
{
    public AxisConventionPresets(AppSystems app)
    {
        InitializeSubsystems();
    }

    /// <summary>The axis presets available when a project's coordinate system is set up.</summary>
    public IEnumerable<IAxisConventionPreset> Presets => Subsystems.OfType<IAxisConventionPreset>();
}
