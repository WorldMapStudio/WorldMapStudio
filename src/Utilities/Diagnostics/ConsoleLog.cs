using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace WorldMapStudio;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>One line the engine wrote to its output, plus any indented detail (a stack trace) that
/// followed it.</summary>
public readonly record struct LogEntry(long Seq, DateTime Time, LogLevel Level, string Message, string? Detail)
{
    public string Raw => Detail is null ? Message : $"{Message}\n{Detail}";
}

/// <summary>
/// Mirror of the editor's console output for the <see cref="LogWindow"/>.
///
/// Godot's own logger writes everything — <c>GD.Print</c>, <c>push_error</c>/<c>push_warning</c>,
/// script and shader errors, unhandled C# exceptions — to <c>user://logs/godot.log</c>, so tailing
/// that file catches the whole stream without every call site having to route through here. The file
/// is the current run's alone (Godot rotates the previous one aside on startup), so it is read from
/// the top.
/// </summary>
public static class ConsoleLog
{
    private const long MaxInitialBytes = 2 * 1024 * 1024;
    private const int MaxPendingLine = 64 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    private static readonly LogStore Store = new();
    private static readonly StringBuilder Pending = new();

    private static FileStream? _stream;
    private static StreamReader? _reader;
    private static long _lastLength;
    private static long _nextPollTicks;
    private static string? _status;

    public static string LogPath { get; } = ProjectSettings.GlobalizePath("user://logs/godot.log");

    public static IReadOnlyList<LogEntry> Entries => Store.Entries;

    public static (int Info, int Warning, int Error) Counts => Store.Counts;

    /// <summary>Null while tailing normally; otherwise why nothing is being read.</summary>
    public static string? Status => _status;

    /// <summary>Reads whatever the engine has written since last frame. Throttled, cheap when idle,
    /// and safe to call every frame whether the window is open or not.</summary>
    public static void Pump()
    {
        long now = System.Environment.TickCount64;
        if (now < _nextPollTicks)
        {
            return;
        }

        _nextPollTicks = now + (long)PollInterval.TotalMilliseconds;

        try
        {
            Poll();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _status = e.Message;
            Close();
        }
    }

    public static void Clear() => Store.Clear();

    /// <summary>The last <paramref name="count"/> lines, newest last, for scripts.</summary>
    public static string[] Recent(int count)
    {
        IReadOnlyList<LogEntry> entries = Store.Entries;
        int take = Math.Clamp(count, 0, entries.Count);
        var lines = new string[take];
        for (int i = 0; i < take; i++)
        {
            lines[i] = entries[entries.Count - take + i].Raw;
        }

        return lines;
    }

    public static void Info(string message) => GD.Print(message);

    public static void Warning(string message) => GD.PushWarning(message);

    public static void Error(string message) => GD.PushError(message);

    private static void Poll()
    {
        if (_stream is null && !Open())
        {
            return;
        }

        if (_stream!.Length < _lastLength)
        {
            // The file was rotated or truncated under us — start over from its head.
            _stream.Seek(0, SeekOrigin.Begin);
            _reader!.DiscardBufferedData();
            Pending.Clear();
            _lastLength = 0;
            Store.Ingest("— log file was rotated —");
        }

        string chunk = _reader!.ReadToEnd();
        _lastLength = _stream.Length;
        if (chunk.Length == 0)
        {
            return;
        }

        Pending.Append(chunk);
        string text = Pending.ToString();
        Pending.Clear();

        int start = 0;
        int newline;
        while ((newline = text.IndexOf('\n', start)) >= 0)
        {
            Store.Ingest(text[start..newline]);
            start = newline + 1;
        }

        int remaining = text.Length - start;
        if (remaining >= MaxPendingLine)
        {
            Store.Ingest(text[start..]);
        }
        else if (remaining > 0)
        {
            Pending.Append(text, start, remaining);
        }
    }

    private static bool Open()
    {
        if (!File.Exists(LogPath))
        {
            _status = $"Waiting for {LogPath}";
            return false;
        }

        _stream = new FileStream(LogPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        _reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        if (_stream.Length > MaxInitialBytes)
        {
            _stream.Seek(-MaxInitialBytes, SeekOrigin.End);
            _reader.DiscardBufferedData();
            Store.Ingest("… earlier log lines skipped …");
        }

        _lastLength = _stream.Length;
        _status = null;
        return true;
    }

    private static void Close()
    {
        _reader?.Dispose();
        _stream?.Dispose();
        _reader = null;
        _stream = null;
    }
}
