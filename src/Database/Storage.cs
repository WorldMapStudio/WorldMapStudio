using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>
/// A named data backend: a dolt repository reached through short-lived EF Core connections. Concrete
/// storages self-register with [Subsystem(nameof(DatabaseSystem))] and host their entity factories
/// (which name the concrete storage type). Dolt speaks the MySQL 8 wire protocol, so contexts are
/// built with the Pomelo MySQL provider against <see cref="Connection"/>.
/// </summary>
public abstract class Storage : ISubsystem
{
    public abstract string Name { get; }

    public virtual float Priority => 0f;

    public StorageConnection Connection { get; } = new();

    /// <summary>The scene-entity factories registered into this storage.</summary>
    public virtual IEnumerable<ISceneEntityFactory> SceneFactories => Enumerable.Empty<ISceneEntityFactory>();

    /// <summary>Creates the storage's tables if missing. A placeholder for the Phase 5 migration flow.</summary>
    public virtual void EnsureSchema() { }

    /// <summary>Builds Pomelo MySQL options for one of this storage's contexts.</summary>
    protected DbContextOptions<TContext> BuildOptions<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseMySql(Connection.BuildConnectionString(), new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
}
