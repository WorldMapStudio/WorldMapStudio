using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Opt-in wall-clock log for the load path, written to the Godot output and to a file.
///
/// A CPU profile is structurally blind to this path: a scan is a chain of awaited database
/// round-trips, and a thread waiting on one is parked in the OS — on no thread, in no sample. Seconds
/// of database latency show up in a sampled trace as an idle process, so the cost has to be measured
/// where it is spent rather than sampled. The file sink exists because that is a lot of lines and an
/// exported build's console is not a practical place to read them.
/// </summary>
public static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static readonly long Origin = Stopwatch.GetTimestamp();
    private static readonly AsyncLocal<string?> CurrentScope = new();

    private static StreamWriter? _file;
    private static bool _enabled;
    private static string _filePath = DefaultFilePath();

    /// <summary>Whether anything is logged at all. Off by default: a scan runs whenever the focus
    /// moves a chunk, so this is loud while flying.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            lock (Gate)
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                Close();

                // Opened here rather than lazily on the first line: the point of the file is to be
                // read afterwards, and "it is at this path, and it exists now" has to be answerable
                // while the run is still set up, not discovered to be wrong once the run is over.
                if (value)
                {
                    WriteLine($"logging enabled, writing to {Path.GetFullPath(_filePath)}");
                }
            }
        }
    }

    /// <summary>Bytes written so far, or -1 when nothing is open. Shown in the performance window so a
    /// log that is silently going nowhere is visible as one.</summary>
    public static long BytesWritten
    {
        get
        {
            lock (Gate)
            {
                return _file?.BaseStream.Length ?? -1L;
            }
        }
    }

    /// <summary>Where the file sink writes. The file is truncated when the first line of a session is
    /// written, so one recording is one whole file rather than an accumulating pile.</summary>
    public static string FilePath
    {
        get => _filePath;
        set
        {
            lock (Gate)
            {
                _filePath = value;
                Close();
            }
        }
    }

    public static long Start() => Stopwatch.GetTimestamp();

    public static double MillisecondsSince(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>
    /// Names the work the calling async flow is doing, so lines from concurrent contexts — a streaming
    /// scan and an image chunk load run at the same time and both hit the database — stay attributable
    /// once they interleave in one file.
    /// </summary>
    public static IDisposable Scope(string name)
    {
        string? previous = CurrentScope.Value;
        CurrentScope.Value = previous is null ? name : $"{previous}/{name}";
        return new ScopeToken(previous);
    }

    public static void Log(string message)
    {
        if (!_enabled)
        {
            return;
        }

        WriteLine(message);
    }

    private static void WriteLine(string message)
    {
        double at = Stopwatch.GetElapsedTime(Origin).TotalSeconds;
        string scope = CurrentScope.Value is { } name ? $" [{name}]" : string.Empty;
        string line = string.Create(CultureInfo.InvariantCulture, $"{at,8:F3}s{scope} {message}");

        lock (Gate)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                _file ??= Open();
                _file.WriteLine(line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _enabled = false;
                Close();
                GD.PushError($"[Diag] Log file '{_filePath}' failed, logging disabled: {e.Message}");
                return;
            }
        }

        GD.Print($"[Diag] {line}");
    }

    private static StreamWriter Open()
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new StreamWriter(_filePath, append: false) { AutoFlush = true };
        file.WriteLine($"# WorldMapStudio diagnostic log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return file;
    }

    private static void Close()
    {
        _file?.Dispose();
        _file = null;
    }

    private static string DefaultFilePath() =>
        Path.Combine(System.Environment.CurrentDirectory, ".local", "log", "wms-diagnostic.log");

    private sealed class ScopeToken : IDisposable
    {
        private readonly string? _previous;

        public ScopeToken(string? previous) => _previous = previous;

        public void Dispose() => CurrentScope.Value = _previous;
    }
}
