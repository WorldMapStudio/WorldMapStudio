using System;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>Covers the async reader/writer lock used to guard storage access during streaming.</summary>
public static class AsyncReaderWriterLockTests
{
    [EditorTest(Category = "Concurrency", Thread = TestThread.Background)]
    public static async Task Multiple_readers_run_concurrently()
    {
        var rwLock = new AsyncReaderWriterLock();
        IDisposable first = await rwLock.ReaderAsync();
        IDisposable second = await rwLock.ReaderAsync(); // must not block behind the first reader

        first.Dispose();
        second.Dispose();
        Assert.IsTrue(true);
    }

    [EditorTest(Category = "Concurrency", Thread = TestThread.Background)]
    public static async Task Writer_waits_for_reader()
    {
        var rwLock = new AsyncReaderWriterLock();
        IDisposable reader = await rwLock.ReaderAsync();

        Task<IDisposable> writer = rwLock.WriterAsync();
        Task finished = await Task.WhenAny(writer, Task.Delay(150));
        Assert.IsFalse(ReferenceEquals(finished, writer), "writer must wait while a reader is held");

        reader.Dispose();
        IDisposable acquired = await writer;
        acquired.Dispose();
    }

    [EditorTest(Category = "Concurrency", Thread = TestThread.Background)]
    public static async Task Reader_waits_for_writer()
    {
        var rwLock = new AsyncReaderWriterLock();
        IDisposable writer = await rwLock.WriterAsync();

        Task<IDisposable> reader = rwLock.ReaderAsync();
        Task finished = await Task.WhenAny(reader, Task.Delay(150));
        Assert.IsFalse(ReferenceEquals(finished, reader), "reader must wait while a writer is held");

        writer.Dispose();
        IDisposable acquired = await reader;
        acquired.Dispose();
    }
}
