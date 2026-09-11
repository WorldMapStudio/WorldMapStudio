using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Last line of defense against silent crashes. Godot's own glue wraps <c>_Ready</c>/<c>_Process</c>
/// in a try/catch that logs to <c>user://logs/godot.log</c> (see <see cref="ConsoleLog"/>), but an
/// exception thrown off the main thread — a bare <c>Task.Run</c>, a background loader thread — never
/// passes through that wrapper; the CLR just tears the process down with nothing written anywhere.
/// These handlers catch that case and get the exception onto disk before the process exits.
/// </summary>
public static class CrashLog
{
    private static int _written;

    [ModuleInitializer]
    internal static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void Write(string source, Exception? exception)
    {
        // Only the first crash matters — once the process is on its way down, later handlers firing
        // (e.g. a second faulted task during teardown) would just overwrite it.
        if (Interlocked.Exchange(ref _written, 1) != 0)
        {
            return;
        }

        string path = Path.Combine(
            System.Environment.CurrentDirectory, ".local", "log", $"wms-crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var text = new StringBuilder()
            .AppendLine($"# WorldMapStudio crash, {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"source: {source}")
            .AppendLine()
            .AppendLine(exception?.ToString() ?? "(no exception object)")
            .ToString();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch
        {
            // The process is already going down over one I/O failure; a second one here has nowhere
            // better to be reported.
        }

        try
        {
            GD.PrintErr(text);
        }
        catch
        {
        }
    }
}
