using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>A column: its name, SQL store type, and nullability. Types are compared for display only
/// (the migration does not alter column types), so equality is by name.</summary>
public sealed record SchemaColumn(string Name, string Type, bool Nullable);

/// <summary>A non-primary index: name, its ordered columns, and whether it is unique.</summary>
public sealed record SchemaIndex(string Name, IReadOnlyList<string> Columns, bool Unique);

/// <summary>One table's structure: columns, primary-key columns (in order), and secondary indexes.</summary>
public sealed record SchemaTable(
    string Name,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<SchemaIndex> Indexes)
{
    public SchemaColumn? Column(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A database's tables, keyed by name (case-insensitive). Produced from either the EF model
/// (expected) or the live database (actual) so the two can be diffed.</summary>
public sealed class Schema
{
    public IReadOnlyDictionary<string, SchemaTable> Tables { get; }

    public Schema(IEnumerable<SchemaTable> tables)
    {
        Tables = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
    }
}
