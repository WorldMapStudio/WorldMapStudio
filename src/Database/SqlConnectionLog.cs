using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WorldMapStudio;

/// <summary>
/// Logs what opening a connection costs, alongside <see cref="SqlCommandLog"/>'s per-command numbers.
/// A short-lived context per unit of work is only cheap while the pool hands back a live connection —
/// this is what says whether it did.
/// </summary>
public sealed class SqlConnectionLog : DbConnectionInterceptor
{
    /// <summary>Shared for the same reason as <see cref="SqlCommandLog.Instance"/>.</summary>
    public static readonly SqlConnectionLog Instance = new();

    private SqlConnectionLog()
    {
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        DiagnosticLog.Log($"sql open  {eventData.Duration.TotalMilliseconds,8:F1}ms");
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ConnectionOpened(connection, eventData);
        return Task.CompletedTask;
    }
}
