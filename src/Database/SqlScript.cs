using System.Collections.Generic;
using System.Text;

namespace WorldMapStudio;

/// <summary>Splits a multi-statement SQL script into individual statements on ';', the way a client
/// tool would rather than a naive <c>string.Split</c> — a semicolon inside a quoted string or
/// backtick-quoted identifier (e.g. a COMMENT clause containing punctuation) does not end the
/// statement early.</summary>
public static class SqlScript
{
    public static IReadOnlyList<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if (quote != null)
            {
                current.Append(c);

                if (c == '\\' && quote != '`' && i + 1 < sql.Length)
                {
                    current.Append(sql[++i]);
                    continue;
                }

                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        current.Append(sql[++i]);
                        continue;
                    }

                    quote = null;
                }

                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
                current.Append(c);
                continue;
            }

            if (c == ';')
            {
                Flush(statements, current);
                continue;
            }

            current.Append(c);
        }

        Flush(statements, current);
        return statements;
    }

    private static void Flush(List<string> statements, StringBuilder current)
    {
        string statement = current.ToString().Trim();
        if (statement.Length > 0)
        {
            statements.Add(statement);
        }

        current.Clear();
    }
}
