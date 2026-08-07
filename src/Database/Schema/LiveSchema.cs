using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;

namespace WorldMapStudio;

/// <summary>Reads the actual structure of a live database from its <c>information_schema</c>.</summary>
public static class LiveSchema
{
    public static async Task<Schema> ReadAsync(string connectionString, string database)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var columns = new Dictionary<string, List<SchemaColumn>>(StringComparer.OrdinalIgnoreCase);

        // Seed table names so empty tables still appear.
        await Query(connection,
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_TYPE='BASE TABLE';",
            database,
            reader => columns[reader.GetString(0)] = []);

        await Query(connection,
            "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA=@db ORDER BY TABLE_NAME, ORDINAL_POSITION;",
            database,
            reader =>
            {
                string table = reader.GetString(0);
                if (!columns.TryGetValue(table, out List<SchemaColumn>? list))
                {
                    columns[table] = list = [];
                }

                list.Add(new SchemaColumn(reader.GetString(1), reader.GetString(2), reader.GetString(3) == "YES"));
            });

        var primaryKeys = new Dictionary<string, List<(int Seq, string Column)>>(StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, Dictionary<string, (bool Unique, List<(int Seq, string Column)> Columns)>>(StringComparer.OrdinalIgnoreCase);

        await Query(connection,
            "SELECT TABLE_NAME, INDEX_NAME, COLUMN_NAME, NON_UNIQUE, SEQ_IN_INDEX FROM information_schema.STATISTICS " +
            "WHERE TABLE_SCHEMA=@db ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;",
            database,
            reader =>
            {
                string table = reader.GetString(0);
                string index = reader.GetString(1);
                string column = reader.GetString(2);
                bool unique = reader.GetInt32(3) == 0;
                int seq = reader.GetInt32(4);

                if (string.Equals(index, "PRIMARY", StringComparison.OrdinalIgnoreCase))
                {
                    if (!primaryKeys.TryGetValue(table, out var pk))
                    {
                        primaryKeys[table] = pk = [];
                    }

                    pk.Add((seq, column));
                }
                else
                {
                    if (!indexes.TryGetValue(table, out var byName))
                    {
                        indexes[table] = byName = new(StringComparer.OrdinalIgnoreCase);
                    }

                    if (!byName.TryGetValue(index, out var entry))
                    {
                        byName[index] = entry = (unique, []);
                    }

                    entry.Columns.Add((seq, column));
                }
            });

        var tables = columns.Select(pair =>
        {
            List<string> pk = primaryKeys.TryGetValue(pair.Key, out var pkCols)
                ? pkCols.OrderBy(c => c.Seq).Select(c => c.Column).ToList()
                : [];

            List<SchemaIndex> tableIndexes = indexes.TryGetValue(pair.Key, out var byName)
                ? byName.Select(i => new SchemaIndex(i.Key, i.Value.Columns.OrderBy(c => c.Seq).Select(c => c.Column).ToList(), i.Value.Unique)).ToList()
                : [];

            return new SchemaTable(pair.Key, pair.Value, pk, tableIndexes);
        });

        return new Schema(tables);
    }

    private static async Task Query(MySqlConnection connection, string sql, string database, Action<MySqlDataReader> onRow)
    {
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@db", database);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            onRow(reader);
        }
    }
}
