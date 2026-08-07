namespace WorldMapStudio;

/// <summary>
/// A named data backend: a dolt repository reached through short-lived EF Core connections. Concrete
/// storages self-register with [Subsystem(nameof(DatabaseSystem))] and host their entity factories
/// (which name the concrete storage type). The EF wiring is added once a dolt instance is available;
/// for now a storage carries its <see cref="Connection"/> config and hosts its factories.
/// </summary>
public abstract class Storage : ISubsystem
{
    public abstract string Name { get; }

    public virtual float Priority => 0f;

    public StorageConnection Connection { get; } = new();
}
