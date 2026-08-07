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
/// the resulting differences plus generated SQL for the <see cref="MigrationWindow"/> to review and
/// apply. Additive changes, drops, and primary-key/index changes are detected; type changes are not.
/// </summary>
public sealed class MigrationSystem
{
    private readonly EditorContext _context;
    private bool _openRequested;

    public MigrationSystem(EditorContext context)
    {
        _context = context;
    }

    public List<StorageMigration> Migrations { get; } = [];

    public bool HasPending => Migrations.Any(migration => migration.HasChanges);

    /// <summary>Recomputes the diff for every storage. Opens the window if anything needs migrating.</summary>
    public void Check()
    {
        Migrations.Clear();
        foreach (Storage storage in _context.Database.Storages)
        {
            if (storage.ExpectedSchema() is not { } expected)
            {
                continue;
            }

            var migration = new StorageMigration(storage);
            try
            {
                Schema live = Run(storage.ReadLiveSchemaAsync);
                migration.Set(SchemaDiff.Compute(expected, live));
            }
            catch (Exception e)
            {
                migration.Error = e.Message;
                GD.PushError($"[Migration] Schema check failed for '{storage.Name}': {e.Message}");
            }

            Migrations.Add(migration);
        }

        _openRequested = HasPending;
    }

    /// <summary>Applies a migration's (possibly user-edited) SQL, then re-checks that storage.</summary>
    public void Apply(StorageMigration migration)
    {
        try
        {
            Run(() => migration.Storage.ApplySqlAsync(migration.Sql));
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
                Schema live = Run(migration.Storage.ReadLiveSchemaAsync);
                migration.Set(SchemaDiff.Compute(expected, live));
            }
        }
        catch (Exception e)
        {
            migration.Error = e.Message;
        }
    }

    /// <summary>True once after a check that found pending changes, so the window can open itself.</summary>
    public bool ConsumeOpenRequest()
    {
        bool requested = _openRequested;
        _openRequested = false;
        return requested;
    }

    // Blocking DB work must run off the Godot main thread's synchronization context (see the deadlock
    // that froze commit); Task.Run keeps the whole async chain on the thread pool.
    private static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

    private static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
}
