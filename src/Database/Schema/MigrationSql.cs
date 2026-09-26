using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorldMapStudio;

/// <summary>Turns a set of <see cref="SchemaChange"/>s into SQLite/DoltLite DDL. The output is a
/// starting point the user can edit before applying, so ordering is best-effort (drops, then creates,
/// then column adds, primary-key fixes, and index creates).</summary>
public static class MigrationSql
{
    public static string Generate(IEnumerable<SchemaChange> changes)
    {
        var builder = new StringBuilder();
        foreach (SchemaChange change in changes.OrderBy(Rank))
        {
            builder.AppendLine(Statement(change));
        }

        return builder.ToString();
    }

    private static int Rank(SchemaChange change) => change.Kind switch
    {
        SchemaChangeKind.DropIndex => 0,
        SchemaChangeKind.DropColumn => 1,
        SchemaChangeKind.DropTable => 2,
        SchemaChangeKind.CreateTable => 3,
        SchemaChangeKind.AddColumn => 4,
        SchemaChangeKind.ChangePrimaryKey => 5,
        SchemaChangeKind.CreateIndex => 6,
        _ => 7,
    };

    private static string Statement(SchemaChange change) => change.Kind switch
    {
        SchemaChangeKind.CreateTable => CreateTable(change.Definition!),
        SchemaChangeKind.DropTable => $"DROP TABLE \"{change.Table}\";",
        SchemaChangeKind.AddColumn => $"ALTER TABLE \"{change.Table}\" ADD COLUMN {AddColumnDef(change.Column!)};",
        SchemaChangeKind.DropColumn => DropColumn(change),
        SchemaChangeKind.ChangePrimaryKey => Rebuild(change.LiveTable!, change.LiveTable! with { PrimaryKey = change.PrimaryKey ?? [] }),
        SchemaChangeKind.CreateIndex => CreateIndexStatement(change.Table, change.Index!),
        SchemaChangeKind.DropIndex => $"DROP INDEX \"{change.Index!.Name}\";",
        _ => string.Empty,
    };

    private static string CreateTable(SchemaTable table)
    {
        // AUTOINCREMENT only works declared inline on a single-column INTEGER PRIMARY KEY; a
        // composite key or a non-auto-increment key gets a separate PRIMARY KEY (...) clause instead.
        SchemaColumn? autoIncrementPk = table.PrimaryKey.Count == 1
            ? table.Column(table.PrimaryKey[0]) is { AutoIncrement: true } column ? column : null
            : null;

        var lines = table.Columns
            .Select(column => column == autoIncrementPk
                ? $"\"{column.Name}\" INTEGER PRIMARY KEY AUTOINCREMENT"
                : ColumnDef(column))
            .ToList();

        if (autoIncrementPk == null && table.PrimaryKey.Count > 0)
        {
            lines.Add($"PRIMARY KEY ({Columns(table.PrimaryKey)})");
        }

        string body = $"CREATE TABLE \"{table.Name}\" (\n  {string.Join(",\n  ", lines)}\n);";
        string indexes = string.Join("\n", table.Indexes.Select(index => CreateIndexStatement(table.Name, index)));
        return indexes.Length == 0 ? body : $"{body}\n{indexes}";
    }

    private static string DropColumn(SchemaChange change)
    {
        SchemaTable live = change.LiveTable!;
        string columnName = change.Column!.Name;

        // SQLite's DROP COLUMN refuses a column that is part of the primary key, an index, or a
        // foreign key; the first two are rebuilt away, the last is left for the user to notice.
        bool needsRebuild = live.PrimaryKey.Contains(columnName, StringComparer.OrdinalIgnoreCase)
            || live.Indexes.Any(index => index.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase));

        if (!needsRebuild)
        {
            return $"ALTER TABLE \"{live.Name}\" DROP COLUMN \"{columnName}\";";
        }

        SchemaTable target = live with
        {
            Columns = live.Columns.Where(c => !string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)).ToList(),
            PrimaryKey = live.PrimaryKey.Where(c => !string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)).ToList(),
            Indexes = live.Indexes.Where(i => !i.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase)).ToList(),
        };

        return Rebuild(live, target);
    }

    /// <summary>
    /// SQLite cannot alter a primary key or drop an indexed/keyed column in place, so both go through
    /// the standard rebuild: a new table with <paramref name="target"/>'s shape, the old rows copied
    /// over by whichever columns exist in both, then a rename over the original.
    /// </summary>
    private static string Rebuild(SchemaTable live, SchemaTable target)
    {
        string tempName = $"{live.Name}__new";
        string columnList = Columns(target.Columns
            .Select(c => c.Name)
            .Where(name => live.Column(name) != null)
            .ToList());

        var builder = new StringBuilder();
        builder.AppendLine("PRAGMA foreign_keys=OFF;");
        builder.AppendLine(CreateTable(target with { Name = tempName }));
        builder.AppendLine($"INSERT INTO \"{tempName}\" ({columnList}) SELECT {columnList} FROM \"{live.Name}\";");
        builder.AppendLine($"DROP TABLE \"{live.Name}\";");
        builder.AppendLine($"ALTER TABLE \"{tempName}\" RENAME TO \"{live.Name}\";");
        foreach (SchemaIndex index in target.Indexes)
        {
            builder.AppendLine(CreateIndexStatement(live.Name, index));
        }

        builder.Append("PRAGMA foreign_keys=ON;");
        return builder.ToString();
    }

    private static string CreateIndexStatement(string table, SchemaIndex index)
    {
        string unique = index.Unique ? "UNIQUE " : string.Empty;
        return $"CREATE {unique}INDEX \"{index.Name}\" ON \"{table}\" ({Columns(index.Columns)});";
    }

    private static string ColumnDef(SchemaColumn column)
    {
        string notNull = column.Nullable ? string.Empty : " NOT NULL";
        return $"\"{column.Name}\" {column.Type}{notNull}";
    }

    // SQLite rejects a NOT NULL column added to an existing table without a non-null default, unlike a
    // fresh CREATE TABLE where there are no rows yet to satisfy.
    private static string AddColumnDef(SchemaColumn column)
    {
        string def = ColumnDef(column);
        return column.Nullable ? def : $"{def} DEFAULT {DefaultLiteral(column.Type)}";
    }

    private static string DefaultLiteral(string type) => type.ToUpperInvariant() switch
    {
        "TEXT" => "''",
        "BLOB" => "X''",
        _ => "0",
    };

    private static string Columns(IEnumerable<string> columns) =>
        string.Join(", ", columns.Select(column => $"\"{column}\""));
}
