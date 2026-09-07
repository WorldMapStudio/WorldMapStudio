using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// A small key/value store, scoped per operation, for whatever an operation needs to remember between
/// runs: a watermark timestamp, a format-version stamp, a handful of rows. Deliberately nothing more.
///
/// This is enough because <see cref="ChunkChangeLog"/> answers "everything changed after this moment",
/// which turns an operation's entire cache into one string per unit it cares about. An operation that
/// genuinely needs per-row bookkeeping declares its own table with <see cref="ITableConfiguration"/>.
/// </summary>
public sealed class BatchState
{
    /// <summary>Where an operation's persisted settings blob lives. Reserved, along with every other
    /// <c>$</c>-prefixed key, so nothing writing state can clobber settings by accident.</summary>
    internal const string SettingsKey = "$settings";

    private readonly EditorContext _context;

    internal BatchState(EditorContext context)
    {
        _context = context;
    }

    /// <summary>A namespace's slice of the store. Keys beginning with <c>$</c> are refused.</summary>
    public BatchOperationState For(string operationId) => new(_context, operationId, allowReservedKeys: false);

    /// <summary>The same slice with the reserved keys writable — for <see cref="BatchSystem"/> itself,
    /// which owns <see cref="SettingsKey"/>.</summary>
    internal BatchOperationState System(string operationId) => new(_context, operationId, allowReservedKeys: true);
}

/// <summary>One namespace's view of <see cref="BatchState"/>, already scoped, so a caller cannot read
/// or write another operation's rows by naming them.</summary>
public sealed class BatchOperationState
{
    private readonly EditorContext _context;
    private readonly bool _allowReservedKeys;

    internal BatchOperationState(EditorContext context, string operationId, bool allowReservedKeys)
    {
        _context = context;
        _allowReservedKeys = allowReservedKeys;
        OperationId = operationId;
    }

    public string OperationId { get; }

    public async Task<string?> GetAsync(string key)
    {
        if (Storage() is not { } storage)
        {
            return null;
        }

        return await storage.LoadBatchStateAsync(OperationId, key).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value)
    {
        Check(key);
        if (Storage() is { } storage)
        {
            await storage.UpsertBatchStateAsync(OperationId, key, value).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(string key)
    {
        Check(key);
        if (Storage() is { } storage)
        {
            await storage.RemoveBatchStateAsync(OperationId, key).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> AllAsync()
    {
        if (Storage() is not { } storage)
        {
            return new Dictionary<string, string>();
        }

        return await storage.LoadAllBatchStateAsync(OperationId).ConfigureAwait(false);
    }

    private void Check(string key)
    {
        if (!_allowReservedKeys && key.StartsWith('$'))
        {
            throw new ArgumentException($"'{key}' is reserved.", nameof(key));
        }
    }

    private EditorStorage? Storage() => _context.Database.Storages.OfType<EditorStorage>().FirstOrDefault();
}
