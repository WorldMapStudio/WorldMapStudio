using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WorldMapStudio;

/// <summary>
/// Logs every SQL command's wall-clock cost to <see cref="DiagnosticLog"/>. Registered on every
/// storage context (see <c>Storage.BuildOptions</c>) as a single shared instance, and inert while the
/// log is off.
///
/// Two numbers per command, because they answer different questions. <c>exec</c> is the round-trip
/// that ends with a reader in hand; <c>read</c> is what streaming and materializing the rows cost on
/// top of it. A query can execute instantly and still take hundreds of milliseconds to read, and only
/// the second number moves when the answer is "you are asking for too many rows".
/// </summary>
public sealed class SqlCommandLog : DbCommandInterceptor
{
    /// <summary>One instance for every context: a fresh interceptor per options object would change
    /// the key EF caches its internal service provider under, rebuilding it per context.</summary>
    public static readonly SqlCommandLog Instance = new();

    // Enough to tell one query from another. The scan's IN-list queries run to tens of kilobytes of
    // SQL, which is itself worth knowing, so the untruncated length is reported alongside.
    private const int SqlPreviewLength = 160;

    private SqlCommandLog()
    {
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogExecuted("reader", command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        LogExecuted("reader", command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogExecuted("nonquery", command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        LogExecuted("nonquery", command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogExecuted("scalar", command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        LogExecuted("scalar", command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
    {
        DiagnosticLog.Log(
            $"sql read  {eventData.Duration.TotalMilliseconds,8:F1}ms  {eventData.ReadCount,6} rows  {Preview(command)}");
        return result;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        DiagnosticLog.Log($"sql FAILED after {eventData.Duration.TotalMilliseconds:F1}ms: {eventData.Exception.Message}");
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData);
        return Task.CompletedTask;
    }

    private static void LogExecuted(string kind, DbCommand command, CommandExecutedEventData eventData)
    {
        DiagnosticLog.Log(
            $"sql exec  {eventData.Duration.TotalMilliseconds,8:F1}ms  {kind,-8}  {Preview(command)}");
    }

    private static string Preview(DbCommand command)
    {
        string sql = command.CommandText.Replace('\r', ' ').Replace('\n', ' ');
        string head = sql.Length <= SqlPreviewLength ? sql : sql[..SqlPreviewLength] + "…";
        return $"({sql.Length} chars, {command.Parameters.Count} params) {head}";
    }
}
