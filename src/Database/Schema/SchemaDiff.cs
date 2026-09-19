using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

public enum SchemaChangeKind
{
    CreateTable,
    DropTable,
    AddColumn,
    DropColumn,
    ChangePrimaryKey,
    CreateIndex,
    DropIndex,
}

/// <summary>One difference between the expected (model) and actual (live) schema, with the payload
/// the SQL generator needs to emit it.</summary>
public sealed record SchemaChange(SchemaChangeKind Kind, string Table)
{
    /// <summary>Full definition for <see cref="SchemaChangeKind.CreateTable"/>.</summary>
    public SchemaTable? Definition { get; init; }

    /// <summary>The column for add/drop-column changes.</summary>
    public SchemaColumn? Column { get; init; }

    /// <summary>The desired primary-key columns for <see cref="SchemaChangeKind.ChangePrimaryKey"/> (empty = drop only).</summary>
    public IReadOnlyList<string>? PrimaryKey { get; init; }

    /// <summary>Whether the live table already has a primary key (so it must be dropped first).</summary>
    public bool HadPrimaryKey { get; init; }

    /// <summary>The index for create/drop-index changes.</summary>
    public SchemaIndex? Index { get; init; }

    /// <summary>True for changes that drop data (dropping a table or column). Surfaced for confirmation.</summary>
    public bool IsDestructive => Kind is SchemaChangeKind.DropTable or SchemaChangeKind.DropColumn;

    /// <summary>One line naming the change, e.g. <c>AddColumn tc_item.Name</c>.</summary>
    public string Describe() => $"{Kind} {Table}{Detail()}";

    private string Detail()
    {
        if (Column != null)
        {
            return $".{Column.Name}";
        }

        return Index != null ? $" [{Index.Name}]" : string.Empty;
    }
}

/// <summary>Compares the expected (EF model) schema to the live database schema. Detects missing/extra
/// tables and columns, primary-key changes, and indexes the model wants that the database lacks.
/// Column type/nullability changes and renames are intentionally ignored, and an index that exists
/// only in the live database is left alone rather than proposed for dropping.</summary>
public static class SchemaDiff
{
    public static List<SchemaChange> Compute(Schema expected, Schema live, IReadOnlySet<string>? ignoreExtraTables = null)
    {
        var changes = new List<SchemaChange>();

        foreach (SchemaTable table in expected.Tables.Values)
        {
            if (!live.Tables.TryGetValue(table.Name, out SchemaTable? liveTable))
            {
                changes.Add(new SchemaChange(SchemaChangeKind.CreateTable, table.Name) { Definition = table });
                continue;
            }

            foreach (SchemaColumn column in table.Columns.Where(c => liveTable.Column(c.Name) == null))
            {
                changes.Add(new SchemaChange(SchemaChangeKind.AddColumn, table.Name) { Column = column });
            }

            foreach (SchemaColumn column in liveTable.Columns.Where(c => table.Column(c.Name) == null))
            {
                changes.Add(new SchemaChange(SchemaChangeKind.DropColumn, table.Name) { Column = column });
            }

            if (!table.PrimaryKey.SequenceEqual(liveTable.PrimaryKey, StringComparer.OrdinalIgnoreCase))
            {
                changes.Add(new SchemaChange(SchemaChangeKind.ChangePrimaryKey, table.Name)
                {
                    PrimaryKey = table.PrimaryKey,
                    HadPrimaryKey = liveTable.PrimaryKey.Count > 0,
                });
            }

            Dictionary<string, SchemaIndex> liveIndexes = liveTable.Indexes.ToDictionary(Signature);
            Dictionary<string, SchemaIndex> expectedIndexes = table.Indexes.ToDictionary(Signature);

            foreach (SchemaIndex index in expectedIndexes.Where(i => !liveIndexes.ContainsKey(i.Key)).Select(i => i.Value))
            {
                changes.Add(new SchemaChange(SchemaChangeKind.CreateIndex, table.Name) { Index = index });
            }
        }

        // A table another storage sharing this database owns (see Storage.OwnsConnection) is not this
        // storage's "extra" table to propose dropping.
        foreach (SchemaTable table in live.Tables.Values
                     .Where(t => !expected.Tables.ContainsKey(t.Name) && !(ignoreExtraTables?.Contains(t.Name) ?? false)))
        {
            changes.Add(new SchemaChange(SchemaChangeKind.DropTable, table.Name));
        }

        return changes;
    }

    // Two indexes are "the same" if they cover the same columns in order with the same uniqueness,
    // regardless of name (EF's generated names need not match a hand-created index).
    private static string Signature(SchemaIndex index) =>
        string.Join(",", index.Columns.Select(c => c.ToLowerInvariant())) + (index.Unique ? "!U" : string.Empty);
}
