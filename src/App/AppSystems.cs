using System;

namespace WorldMapStudio;

/// <summary>
/// Root host for what has to exist before a project does: the project-select screens and
/// <see cref="CreateProjectOperation"/> run against it with no <see cref="EditorContext"/> yet.
/// App-lifetime, like <see cref="WorkQueue"/> and <see cref="EditorStyle"/>; everything
/// project-scoped hangs off <see cref="EditorContext"/> instead. A subsystem's constructor must not
/// read <see cref="Instance"/> — it is still being built.
/// </summary>
public sealed partial class AppSystems : ISubsystemHost
{
    private static readonly Lazy<AppSystems> LazyInstance = new(() => new AppSystems());

    public static AppSystems Instance => LazyInstance.Value;

    private AppSystems()
    {
        InitializeSubsystems();
    }
}
