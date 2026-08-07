using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace WorldMapStudio;

/// <summary>Extracts the schema a storage's EF Core context expects, via EF's relational model.</summary>
public static class ModelSchema
{
    public static Schema Extract(DbContext context)
    {
        IRelationalModel relational = context.Model.GetRelationalModel();
        var tables = new List<SchemaTable>();

        foreach (ITable table in relational.Tables)
        {
            var columns = table.Columns
                .Select(column => new SchemaColumn(column.Name, column.StoreType, column.IsNullable, IsAutoIncrement(column)))
                .ToList();

            var primaryKey = table.PrimaryKey?.Columns.Select(column => column.Name).ToList() ?? [];

            var indexes = table.Indexes
                .Select(index => new SchemaIndex(
                    index.Name ?? string.Empty,
                    index.Columns.Select(column => column.Name).ToList(),
                    index.IsUnique))
                .ToList();

            tables.Add(new SchemaTable(table.Name, columns, primaryKey, indexes));
        }

        return new Schema(tables);
    }

    // An integer column whose value EF generates on insert is an AUTO_INCREMENT identity column.
    private static bool IsAutoIncrement(IColumn column) =>
        column.StoreType.Contains("int", StringComparison.OrdinalIgnoreCase)
        && column.PropertyMappings.Any(mapping => mapping.Property.ValueGenerated == ValueGenerated.OnAdd);
}
