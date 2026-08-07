using System;
using System.Threading;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// An async reader/writer lock: any number of readers may hold it at once, but a writer holds it
/// exclusively. Scene streaming acquires the reader (concurrent DB reads); a commit acquires the
/// writer (exclusive). Await the acquire, then dispose the returned handle to release.
/// </summary>
public sealed class AsyncReaderWriterLock
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _gate = new();
    private int _readerCount;
    private TaskCompletionSource<bool>? _drained;

    public async Task<IDisposable> ReaderAsync(CancellationToken cancellationToken = default)
    {
        // Briefly take the write lock so no reader can register while a writer holds it, then register.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _readerCount++;
        }

        _writeLock.Release();
        return new Releaser(this, writer: false);
    }

    public async Task<IDisposable> WriterAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Holding the write lock blocks new readers; now wait for existing readers to drain.
        while (true)
        {
            Task drained;
            lock (_gate)
            {
                if (_readerCount == 0)
                {
                    return new Releaser(this, writer: true);
                }

                _drained ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                drained = _drained.Task;
            }

            await drained.ConfigureAwait(false);
        }
    }

    private void ReleaseReader()
    {
        lock (_gate)
        {
            if (--_readerCount == 0 && _drained != null)
            {
                _drained.SetResult(true);
                _drained = null;
            }
        }
    }

    private void ReleaseWriter() => _writeLock.Release();

    private sealed class Releaser(AsyncReaderWriterLock owner, bool writer) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            if (writer)
            {
                owner.ReleaseWriter();
            }
            else
            {
                owner.ReleaseReader();
            }
        }
    }
}
