using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the axis convention presets offered when a project's coordinate system is set up. Unlike
/// most subsystem hosts this is not scoped to an open project — <see cref="CreateProjectOperation"/>
/// needs the list before a <see cref="Project"/>, let alone an <c>EditorContext</c>, exists — so it is
/// a lazily-constructed singleton instead of a member of one. Landscape settings solve the same
/// "plugins contribute presets" problem with <see cref="ILandscapeProfile"/> hosted on the (project-
/// scoped) <c>LandscapeSystem</c>; this is that pattern's counterpart for the one setting that has to
/// exist before a project does.
/// </summary>
public sealed partial class AxisConventionPresets : ISubsystemHost
{
    private static readonly Lazy<AxisConventionPresets> LazyInstance = new(() => new AxisConventionPresets());

    public static AxisConventionPresets Instance => LazyInstance.Value;

    private AxisConventionPresets()
    {
        InitializeSubsystems();
    }

    /// <summary>The axis presets available when a project's coordinate system is set up.</summary>
    public IEnumerable<IAxisConventionPreset> Presets => Subsystems.OfType<IAxisConventionPreset>();
}
