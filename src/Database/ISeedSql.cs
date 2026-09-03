namespace WorldMapStudio;

/// <summary>
/// A one-time SQL script a storage applies to its live database the first time a project opens
/// against it — reference/seed data, not the schema-drift migrations <see cref="MigrationSystem"/>
/// tracks. Registers with <c>[Subsystem(nameof(SomeStorage))]</c> like any other storage facet;
/// <see cref="Storage.Seeds"/> gathers every registered instance and <see cref="Storage.ApplySeedsAsync"/>
/// runs each one at most once per storage database, recording it by <see cref="Name"/> in that
/// database's own seed-history table (<see cref="Storage.SeedHistoryTableName"/>).
/// </summary>
public interface ISeedSql : ISubsystem
{
    /// <summary>Stable identifier recorded in the seed-history table once this seed has run.
    /// Changing it re-applies the seed as if it were new.</summary>
    string Name { get; }

    /// <summary>The SQL to run, once, split into statements by <see cref="SqlScript.SplitStatements"/>.</summary>
    string Sql { get; }
}
