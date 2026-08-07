using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorldMapStudio;

/// <summary>Turns a set of <see cref="SchemaChange"/>s into MySQL/dolt DDL. The output is a starting
/// point the user can edit before applying, so ordering is best-effort (drops, then creates, then
/// column adds, primary-key fixes, and index creates).</summary>
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
        SchemaChangeKind.DropTable => $"DROP TABLE `{change.Table}`;",
        SchemaChangeKind.AddColumn => $"ALTER TABLE `{change.Table}` ADD COLUMN {ColumnDef(change.Column!)};",
        SchemaChangeKind.DropColumn => $"ALTER TABLE `{change.Table}` DROP COLUMN `{change.Column!.Name}`;",
        SchemaChangeKind.ChangePrimaryKey => PrimaryKey(change),
        SchemaChangeKind.CreateIndex => CreateIndex(change),
        SchemaChangeKind.DropIndex => $"DROP INDEX `{change.Index!.Name}` ON `{change.Table}`;",
        _ => string.Empty,
    };

    private static string CreateTable(SchemaTable table)
    {
        var lines = table.Columns.Select(ColumnDef).ToList();
        if (table.PrimaryKey.Count > 0)
        {
            lines.Add($"PRIMARY KEY ({Columns(table.PrimaryKey)})");
        }

        foreach (SchemaIndex index in table.Indexes)
        {
            lines.Add($"{(index.Unique ? "UNIQUE " : string.Empty)}KEY `{index.Name}` ({Columns(index.Columns)})");
        }

        return $"CREATE TABLE `{table.Name}` (\n  {string.Join(",\n  ", lines)}\n);";
    }

    private static string PrimaryKey(SchemaChange change)
    {
        var builder = new StringBuilder();
        if (change.HadPrimaryKey)
        {
            builder.AppendLine($"ALTER TABLE `{change.Table}` DROP PRIMARY KEY;");
        }

        if (change.PrimaryKey is { Count: > 0 })
        {
            builder.Append($"ALTER TABLE `{change.Table}` ADD PRIMARY KEY ({Columns(change.PrimaryKey)});");
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreateIndex(SchemaChange change)
    {
        SchemaIndex index = change.Index!;
        string unique = index.Unique ? "UNIQUE " : string.Empty;
        return $"CREATE {unique}INDEX `{index.Name}` ON `{change.Table}` ({Columns(index.Columns)});";
    }

    private static string ColumnDef(SchemaColumn column)
    {
        var builder = new StringBuilder($"`{column.Name}` {column.Type}");
        if (!column.Nullable)
        {
            builder.Append(" NOT NULL");
        }

        if (column.AutoIncrement)
        {
            builder.Append(" AUTO_INCREMENT");
        }

        return builder.ToString();
    }

    private static string Columns(IEnumerable<string> columns) =>
        string.Join(", ", columns.Select(column => $"`{column}`"));
}
