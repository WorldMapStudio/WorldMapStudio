namespace WorldMapStudio;

/// <summary>
/// Marks the real OS thread <see cref="WorldMapStudioApp"/> starts on, so a debug-only check elsewhere
/// (see <see cref="AsyncReaderWriterLock"/>) can tell whether it is running on it. A
/// <c>[ThreadStatic]</c> flag rather than <c>SynchronizationContext.Current</c>: <c>Task.Run</c> already
/// guarantees background database work lands on a different real thread, so the flag is correct there
/// with no need to also special-case <see cref="BlockingWork"/>.
/// </summary>
public static class MainThread
{
    [System.ThreadStatic]
    private static bool _isMain;

    public static void Mark() => _isMain = true;

    public static bool IsCurrent => _isMain;
}
