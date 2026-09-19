using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Schema drift between the code and the live databases, exposed to JS as <c>wms.migrations</c>. Read
/// only: applying a migration under an open world is what the Migration screen exists to prevent, so
/// pending changes are applied by restarting into it.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class MigrationsScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "migrations";

    public MigrationsScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Compares every storage's schema to its database again, e.g. after a pull changed it,
    /// and returns whether anything is pending.</summary>
    [ScriptFunction]
    public bool Check()
    {
        _context.Migrations.Check();
        return _context.Migrations.HasPending;
    }

    /// <summary>What <see cref="Check"/> last found, for each storage that has changes or failed to check.</summary>
    [ScriptFunction]
    public MigrationDescriptor[] List() =>
        _context.Migrations.Migrations
            .Where(migration => migration.HasChanges || migration.Error != null)
            .Select(migration => new MigrationDescriptor(migration))
            .ToArray();
}

/// <summary>A <see cref="StorageMigration"/> as <c>wms.migrations</c> reports it.</summary>
public sealed class MigrationDescriptor
{
    public MigrationDescriptor(StorageMigration migration)
    {
        Storage = migration.Storage.Name;
        Changes = migration.Changes.Select(change => change.Describe()).ToArray();
        Sql = migration.Sql;
        Error = migration.Error;
    }

    [ScriptProperty] public string Storage { get; }
    [ScriptProperty] public string[] Changes { get; }
    [ScriptProperty] public string Sql { get; }
    [ScriptProperty] public string? Error { get; }
}
