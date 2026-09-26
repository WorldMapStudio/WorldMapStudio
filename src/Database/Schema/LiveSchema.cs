using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WorldMapStudio;

/// <summary>Reads the actual structure of a live DoltLite/SQLite database from its pragmas.</summary>
public static class LiveSchema
{
    public static async Task<Schema> ReadAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tableNames = new List<string>();
        await Query(connection,
            "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'dolt_%';",
            reader => tableNames.Add(reader.GetString(0)));

        var tables = new List<SchemaTable>();
        foreach (string table in tableNames)
        {
            var columns = new List<SchemaColumn>();
            await Query(connection,
                $"SELECT name, type, \"notnull\" FROM pragma_table_info('{Escape(table)}');",
                reader => columns.Add(new SchemaColumn(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) == 0)));

            var primaryKey = new List<(int Seq, string Column)>();
            await Query(connection,
                $"SELECT name, pk FROM pragma_table_info('{Escape(table)}') WHERE pk > 0;",
                reader => primaryKey.Add((reader.GetInt32(1), reader.GetString(0))));

            var indexes = new List<SchemaIndex>();
            await Query(connection,
                $"SELECT name, \"unique\", origin FROM pragma_index_list('{Escape(table)}');",
                reader =>
                {
                    string indexName = reader.GetString(0);
                    bool unique = reader.GetInt64(1) != 0;
                    string origin = reader.GetString(2);
                    if (origin == "pk")
                    {
                        // Backs the primary key (e.g. sqlite_autoindex_*); already covered above.
                        return;
                    }

                    var indexColumns = new List<(int Seq, string Column)>();
                    QuerySync(connection,
                        $"SELECT seqno, name FROM pragma_index_info('{Escape(indexName)}');",
                        indexReader => indexColumns.Add((indexReader.GetInt32(0), indexReader.GetString(1))));

                    indexes.Add(new SchemaIndex(indexName, indexColumns.OrderBy(c => c.Seq).Select(c => c.Column).ToList(), unique));
                });

            tables.Add(new SchemaTable(
                table,
                columns,
                primaryKey.OrderBy(c => c.Seq).Select(c => c.Column).ToList(),
                indexes));
        }

        return new Schema(tables);
    }

    private static async Task Query(SqliteConnection connection, string sql, Action<SqliteDataReader> onRow)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            onRow(reader);
        }
    }

    // A pragma table-valued function can't be queried while another reader on the same connection is
    // open, but this one only ever nests inside the (synchronous, buffered) callback above.
    private static void QuerySync(SqliteConnection connection, string sql, Action<SqliteDataReader> onRow)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            onRow(reader);
        }
    }

    private static string Escape(string identifier) => identifier.Replace("'", "''");
}
