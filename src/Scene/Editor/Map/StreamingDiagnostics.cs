using System.Diagnostics;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Opt-in wall-clock logging for the streaming scan, toggled from the performance window.
///
/// A CPU profile is structurally blind to this path: a scan is a chain of awaited database
/// round-trips, and a thread waiting on one is parked in the OS, on no thread and in no sample. A
/// scan that takes seconds shows up in a trace as an idle process.
/// </summary>
public static class StreamingDiagnostics
{
    /// <summary>Whether scan timings are logged. Off by default — a scan runs whenever the focus
    /// moves a chunk, so this is loud while flying.</summary>
    public static bool Enabled { get; set; }

    public static long Start() => Stopwatch.GetTimestamp();

    public static double MillisecondsSince(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    public static void Log(string message)
    {
        if (Enabled)
        {
            GD.Print($"[Scan] {message}");
        }
    }
}
