using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>A storage's pending schema changes and the editable SQL to apply them.</summary>
public sealed class StorageMigration
{
    public StorageMigration(Storage storage)
    {
        Storage = storage;
    }

    public Storage Storage { get; }

    public List<SchemaChange> Changes { get; private set; } = [];

    /// <summary>Editable SQL buffer (bound to the migration window's text field).</summary>
    public string Sql = string.Empty;

    public string? Error;

    public bool HasChanges => Changes.Count > 0;

    public void Set(List<SchemaChange> changes)
    {
        Changes = changes;
        Sql = MigrationSql.Generate(changes);
        Error = null;
    }
}

/// <summary>
/// Compares each storage's EF model schema to its live database on startup (and on demand), and holds
/// the resulting differences plus generated SQL for the <see cref="Migration"/> scene to review and
/// apply. Additive changes, drops, and primary-key/index changes are detected; type changes are not.
/// </summary>
public sealed class MigrationSystem
{
    private readonly EditorContext _context;

    public MigrationSystem(EditorContext context)
    {
        _context = context;
    }

    public List<StorageMigration> Migrations { get; } = [];

    public bool HasPending => Migrations.Any(migration => migration.HasChanges);

    /// <summary>Recomputes the diff for every storage.</summary>
    public void Check()
    {
        Migrations.Clear();
        List<Storage> storages = _context.Database.Storages.ToList();

        foreach (Storage storage in storages)
        {
            if (storage.ExpectedSchema() is not { } expected)
            {
                continue;
            }

            var migration = new StorageMigration(storage);
            try
            {
                Schema live = BlockingWork.Run(storage.ReadLiveSchemaAsync);
                migration.Set(SchemaDiff.Compute(expected, live, SiblingTables(storage, storages)));
            }
            catch (Exception e)
            {
                migration.Error = e.Message;
                GD.PushError($"[Migration] Schema check failed for '{storage.Name}': {e.Message}");
            }

            Migrations.Add(migration);
        }
    }

    /// <summary>Applies a migration's (possibly user-edited) SQL, then re-checks that storage.</summary>
    public void Apply(StorageMigration migration)
    {
        try
        {
            BlockingWork.Run(() => migration.Storage.ApplySqlAsync(migration.Sql));
        }
        catch (Exception e)
        {
            migration.Error = e.Message;
            GD.PushError($"[Migration] Apply failed for '{migration.Storage.Name}': {e.Message}");
            return;
        }

        try
        {
            if (migration.Storage.ExpectedSchema() is { } expected)
            {
                List<Storage> storages = _context.Database.Storages.ToList();
                Schema live = BlockingWork.Run(migration.Storage.ReadLiveSchemaAsync);
                migration.Set(SchemaDiff.Compute(expected, live, SiblingTables(migration.Storage, storages)));
            }
        }
        catch (Exception e)
        {
            migration.Error = e.Message;
        }
    }

    // Tables owned by another storage that shares this one's connection (see Storage.OwnsConnection) —
    // e.g. CataStorage sharing EditorStorage's database — aren't this storage's tables to propose
    // dropping just because its own EF model doesn't declare them. Storage.SeedHistoryTableName is
    // WorldMapStudio's own bookkeeping, not something any EF model declares either, so it gets the
    // same exemption regardless of connection sharing.
    private static IReadOnlySet<string> SiblingTables(Storage storage, IReadOnlyList<Storage> storages) =>
        storages
            .Where(other => other != storage && ReferenceEquals(other.Connection, storage.Connection))
            .SelectMany(other => other.ExpectedSchema()?.Tables.Keys ?? Enumerable.Empty<string>())
            .Append(Storage.SeedHistoryTableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
