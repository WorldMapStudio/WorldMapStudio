#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;
using Microsoft.Diagnostics.NETCore.Client;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

[Subsystem(nameof(WindowManager))]
public sealed class PerformanceWindow : Window
{
    public override string? Category => "Debug";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.P, ShortcutModifiers.Alt);

    private static readonly NVector4 OkColor = new(0.42f, 0.85f, 0.46f, 1.0f);
    private static readonly NVector4 WarnColor = new(1.0f, 0.72f, 0.22f, 1.0f);
    private static readonly NVector4 ErrorColor = new(1.0f, 0.45f, 0.40f, 1.0f);
    private static readonly NVector4 MutedColor = new(0.62f, 0.62f, 0.62f, 1.0f);

    private readonly string _workingDirectory;

    private TraceFormat _format = TraceFormat.Speedscope;
    private TraceProfile _profile = TraceProfile.CpuSampling;
    private int _processId = System.Environment.ProcessId;
    private int _bufferSizeMb = 256;
    private string _providers = "";
    private string _outputDirectory = Path.Combine(System.Environment.CurrentDirectory, ".traces");
    private string _traceName = "wms-trace";

    private bool _toolCheckStarted;
    private bool _toolCheckComplete;
    private bool _toolAvailable;
    private TraceToolMode _toolMode = TraceToolMode.None;
    private string _toolStatus = "Checking for dotnet-trace...";

    private EventPipeSession? _session;
    private Task? _copyTask;
    private DateTime _traceStartedAt;
    private string? _activeNetTracePath;
    private string? _activeOpenPath;
    private string? _activeConvertBasePath;
    private string? _lastOutputPath;
    private string? _lastError;
    private bool _stopRequested;
    private readonly Dictionary<string, string> _servedTraceFiles = new();
    private TcpListener? _traceFileServer;
    private int _traceFileServerPort;

    public PerformanceWindow(WindowManager manager)
        : base("Performance", defaultSize: new NVector2(620.0f, 460.0f))
    {
        _workingDirectory = System.Environment.CurrentDirectory;
    }

    protected override void OnBeforeDraw()
    {
        if (!_toolCheckStarted)
        {
            RefreshToolAvailability();
        }

        if (_copyTask is { IsCompleted: true })
        {
            CompleteTrace();
        }
    }

    protected override void DrawContent()
    {
        ImGui.Text($"FPS: {Engine.GetFramesPerSecond():F1}");
        DrawRenderStats();
        ImGui.Separator();

        DrawToolStatus();
        ImGui.Separator();

        DrawSettings();
        ImGui.Separator();

        DrawTraceControls();
        DrawTraceStatus();
    }

    // These come from Godot's own RenderingServer accounting, not a dotnet-trace capture — so unlike
    // CPU sampling, they see cost that lands on the native rendering thread (e.g. draw submission for
    // thousands of uninstanced MeshInstance3Ds) that a .NET-only trace is structurally blind to. The
    // custom ImGui backend (ImGuiRenderer) issues its draws directly through RenderingDevice rather
    // than through the RenderingServer instance pipeline these monitors track, so this is close to a
    // clean read on the 3D scene's own submission cost, not the editor UI's.
    private void DrawRenderStats()
    {
        double drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        double primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        ImGui.Text($"Draw calls: {drawCalls:F0}   Objects: {objects:F0}   Primitives: {primitives:F0}");
    }

    private void DrawToolStatus()
    {
        ImGui.Text("dotnet-trace");
        ImGui.SameLine();
        ImGui.TextColored(_toolAvailable ? OkColor : (_toolCheckComplete ? ErrorColor : WarnColor), _toolStatus);
        ImGui.SameLine();
        ImGui.BeginDisabled(IsRecording);
        if (ImGui.SmallButton("Refresh"))
        {
            RefreshToolAvailability();
        }
        ImGui.EndDisabled();
    }

    private void DrawSettings()
    {
        ImGui.BeginDisabled(IsRecording);

        ImGui.PushItemWidth(160.0f);
        ImGui.InputInt("Process Id", ref _processId);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Defaults to the running editor process.");
        }

        ImGui.InputInt("Buffer size (MB)", ref _bufferSizeMb);
        _bufferSizeMb = Math.Clamp(_bufferSizeMb, 1, 4096);
        ImGui.PopItemWidth();

        int format = (int)_format;
        ImGui.PushItemWidth(180.0f);
        if (ImGui.Combo("Format", ref format, "Speedscope\0NetTrace\0"))
        {
            _format = (TraceFormat)format;
        }

        int profile = (int)_profile;
        if (ImGui.Combo("Profile", ref profile, "CPU sampling\0GC verbose\0GC collect\0"))
        {
            _profile = (TraceProfile)profile;
        }
        ImGui.PopItemWidth();

        ImGui.InputText("Output directory", ref _outputDirectory, 512);
        ImGui.InputText("Trace name", ref _traceName, 128);
        ImGui.InputTextWithHint("Providers", "Optional EventPipe providers, comma-separated", ref _providers, 1024);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Leave empty to use the selected profile. Example: Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5");
        }

        ImGui.EndDisabled();
    }

    private void DrawTraceControls()
    {
        bool canStart = _toolAvailable && _toolCheckComplete && !IsRecording && _processId > 0;

        if (!IsRecording)
        {
            ImGui.BeginDisabled(!canStart);
            if (ImGui.Button("Start trace", new NVector2(140.0f, 0.0f)))
            {
                StartTrace();
            }
            if (!canStart && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(StartBlocker());
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled(_stopRequested);
            if (ImGui.Button(_stopRequested ? "Stopping..." : "Stop trace", new NVector2(140.0f, 0.0f)))
            {
                StopTrace();
            }
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(_lastOutputPath == null || IsRecording);
        if (ImGui.Button("Open last"))
        {
            OpenTrace(_lastOutputPath!);
        }
        ImGui.EndDisabled();
    }

    private void DrawTraceStatus()
    {
        if (IsRecording)
        {
            TimeSpan elapsed = DateTime.Now - _traceStartedAt;
            ImGui.TextColored(_stopRequested ? WarnColor : OkColor, _stopRequested ? $"Stopping {elapsed:mm\\:ss}" : $"Recording {elapsed:mm\\:ss}");
            if (_activeNetTracePath != null)
            {
                ImGui.TextWrapped(_activeNetTracePath);
            }
            return;
        }

        if (_lastOutputPath != null)
        {
            ImGui.TextColored(OkColor, "Last trace");
            ImGui.TextWrapped(_lastOutputPath);
        }
        else
        {
            ImGui.TextColored(MutedColor, "No trace captured yet.");
        }

        if (!string.IsNullOrEmpty(_lastError))
        {
            ImGui.TextColored(ErrorColor, _lastError);
        }
    }

    private bool IsRecording => _session != null || _copyTask != null;

    private void RefreshToolAvailability()
    {
        _toolCheckStarted = true;
        _toolCheckComplete = false;
        _toolAvailable = false;
        _toolMode = TraceToolMode.None;
        _toolStatus = "Checking for dotnet-trace...";

        try
        {
            using Process process = StartProcess("dotnet", "tool list --local", redirectOutput: true);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _toolStatus = "dotnet tool list timed out";
                return;
            }

            if (process.ExitCode == 0 && output.IndexOf("dotnet-trace", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _toolAvailable = true;
                _toolMode = TraceToolMode.LocalTool;
                _toolStatus = "dotnet-trace found as local tool";
                return;
            }

            if (process.ExitCode != 0 && error.Length > 0)
            {
                _toolStatus = error.Trim();
                return;
            }

            CheckPathTool();
        }
        catch (Exception ex)
        {
            _toolStatus = ex.Message;
        }
        finally
        {
            _toolCheckComplete = true;
        }
    }

    private void CheckPathTool()
    {
        try
        {
            using Process process = StartProcess("dotnet-trace", "--version", redirectOutput: true);
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _toolStatus = "dotnet-trace --version timed out";
                return;
            }

            _toolAvailable = process.ExitCode == 0;
            _toolMode = _toolAvailable ? TraceToolMode.PathCommand : TraceToolMode.None;
            _toolStatus = _toolAvailable
                ? $"dotnet-trace found on PATH ({output})"
                : "dotnet-trace not found locally or on PATH";

            if (!_toolAvailable && error.Length > 0)
            {
                _toolStatus = error;
            }
        }
        catch
        {
            _toolAvailable = false;
            _toolMode = TraceToolMode.None;
            _toolStatus = "dotnet-trace not found locally or on PATH";
        }
    }

    private void StartTrace()
    {
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            (_activeConvertBasePath, _activeNetTracePath, _activeOpenPath) = BuildOutputPaths();
            _lastError = null;
            _stopRequested = false;

            DiagnosticsClient client = new(_processId);
            _session = client.StartEventPipeSession(BuildProviders(), requestRundown: true, circularBufferMB: _bufferSizeMb);
            EventPipeSession session = _session;
            string netTracePath = _activeNetTracePath;

            _copyTask = Task.Run(() =>
            {
                using FileStream output = File.Create(netTracePath);
                session.EventStream.CopyTo(output);
            });
            _traceStartedAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            _session?.Dispose();
            _session = null;
            _copyTask = null;
            _lastError = ex.Message;
        }
    }

    private void StopTrace()
    {
        EventPipeSession? session = _session;
        if (session == null)
        {
            return;
        }

        _stopRequested = true;
        Task.Run(() =>
        {
            try
            {
                session.Stop();
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to stop trace: {ex.Message}";
            }
        });
    }

    private void CompleteTrace()
    {
        Task copyTask = _copyTask!;
        string? openPath = _activeOpenPath;
        string? netTracePath = _activeNetTracePath;
        string? convertBasePath = _activeConvertBasePath;

        _copyTask = null;
        _session?.Dispose();
        _session = null;
        _activeNetTracePath = null;
        _activeOpenPath = null;
        _activeConvertBasePath = null;
        _stopRequested = false;

        if (copyTask.IsFaulted)
        {
            _lastError = copyTask.Exception?.GetBaseException().Message ?? "Trace capture failed.";
            return;
        }

        if (openPath == null || netTracePath == null)
        {
            return;
        }

        if (_format == TraceFormat.Speedscope)
        {
            if (convertBasePath == null || !ConvertTrace(netTracePath, convertBasePath))
            {
                return;
            }
        }

        if (File.Exists(openPath))
        {
            _lastOutputPath = openPath;
            OpenTrace(openPath);
        }
        else
        {
            _lastError = $"The expected trace file was not found: {openPath}";
        }
    }

    private bool ConvertTrace(string netTracePath, string outputBasePath)
    {
        try
        {
            (string fileName, string arguments) = BuildConvertCommand(netTracePath, outputBasePath);
            using Process process = StartProcess(fileName, arguments, redirectOutput: true);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _lastError = "dotnet-trace convert timed out.";
                return false;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            string details = error.Trim().Length > 0 ? error.Trim() : output.Trim();
            _lastError = details.Length > 0
                ? $"dotnet-trace convert exited with code {process.ExitCode}: {details}"
                : $"dotnet-trace convert exited with code {process.ExitCode}.";
            return false;
        }
        catch (Exception ex)
        {
            _lastError = $"Trace was captured, but conversion failed: {ex.Message}";
            return false;
        }
    }

    private IReadOnlyList<EventPipeProvider> BuildProviders()
    {
        if (!string.IsNullOrWhiteSpace(_providers))
        {
            return ParseProviders(_providers);
        }

        return _profile switch
        {
            TraceProfile.GcVerbose => new[]
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 0x1C00008001L),
            },
            TraceProfile.GcCollect => new[]
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x1L),
            },
            _ => new[]
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x100003801DL),
                new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, unchecked((long)0xF00000000000)),
            },
        };
    }

    private static IReadOnlyList<EventPipeProvider> ParseProviders(string providers)
    {
        List<EventPipeProvider> parsed = new();
        foreach (string item in providers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = item.Split(':');
            string name = parts[0].Trim();
            long keywords = parts.Length > 1 && TryParseLong(parts[1], out long k) ? k : -1L;
            EventLevel level = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int l)
                ? (EventLevel)l
                : EventLevel.Informational;

            if (name.Length > 0)
            {
                parsed.Add(new EventPipeProvider(name, level, keywords));
            }
        }

        return parsed;
    }

    private (string ConvertBasePath, string NetTracePath, string OpenPath) BuildOutputPaths()
    {
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(_traceName) ? "wms-trace" : _traceName.Trim());
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string basePath = Path.Combine(_outputDirectory, $"{safeName}-{stamp}");
        string netTracePath = basePath + ".nettrace";
        string openPath = _format == TraceFormat.Speedscope ? basePath + ".speedscope.json" : netTracePath;
        return (basePath, netTracePath, openPath);
    }

    private (string FileName, string Arguments) BuildConvertCommand(string netTracePath, string outputBasePath)
    {
        string command = _toolMode == TraceToolMode.LocalTool
            ? $"tool run dotnet-trace -- convert {Quote(netTracePath)}"
            : $"convert {Quote(netTracePath)}";

        string args = $"{command} --format Speedscope --output {Quote(outputBasePath)}";
        return _toolMode == TraceToolMode.LocalTool ? ("dotnet", args) : ("dotnet-trace", args);
    }

    private Process StartProcess(string fileName, string arguments, bool redirectOutput)
    {
        ProcessStartInfo info = new(fileName, arguments)
        {
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
        };

        return Process.Start(info) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
    }

    private string StartBlocker()
    {
        if (_processId <= 0)
        {
            return "Enter a valid process id.";
        }

        return _toolAvailable
            ? "Waiting for dotnet-trace check to finish."
            : "dotnet-trace is not installed as a local tool and was not found on PATH.";
    }

    private void OpenTrace(string path)
    {
        try
        {
            if (path.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase))
            {
                string profileUri = ServeTraceFile(path);
                OpenShell($"https://www.speedscope.app/#profileURL={Uri.EscapeDataString(profileUri)}");
                return;
            }

            OpenShell(path);
        }
        catch (Exception ex)
        {
            _lastError = $"Trace was saved, but opening it failed: {ex.Message}";
        }
    }

    private static void OpenShell(string target)
    {
        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
        });
    }

    private string ServeTraceFile(string path)
    {
        EnsureTraceFileServer();
        string id = Guid.NewGuid().ToString("N");
        _servedTraceFiles[id] = Path.GetFullPath(path);
        return $"http://127.0.0.1:{_traceFileServerPort}/trace/{id}";
    }

    private void EnsureTraceFileServer()
    {
        if (_traceFileServer != null)
        {
            return;
        }

        _traceFileServer = new TcpListener(IPAddress.Loopback, 0);
        _traceFileServer.Start();
        _traceFileServerPort = ((IPEndPoint)_traceFileServer.LocalEndpoint).Port;
        _ = Task.Run(ServeTraceFiles);
    }

    private async Task ServeTraceFiles()
    {
        TcpListener server = _traceFileServer!;
        while (true)
        {
            TcpClient client;
            try
            {
                client = await server.AcceptTcpClientAsync();
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => ServeTraceFileRequest(client));
        }
    }

    private async Task ServeTraceFileRequest(TcpClient client)
    {
        using (client)
        {
            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync();
                if (requestLine == null)
                {
                    return;
                }

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                string? path = ResolveTraceRequest(requestLine);
                if (path == null || !File.Exists(path))
                {
                    await WriteHttpResponse(stream, "404 Not Found", "text/plain", Encoding.UTF8.GetBytes("Trace file not found."));
                    return;
                }

                await WriteHttpResponse(stream, "200 OK", "application/json", await File.ReadAllBytesAsync(path));
            }
            catch
            {
                // Browser fetch errors will surface in Speedscope; nothing useful to report in-frame here.
            }
        }
    }

    private string? ResolveTraceRequest(string requestLine)
    {
        string[] parts = requestLine.Split(' ');
        if (parts.Length < 2 || !parts[1].StartsWith("/trace/", StringComparison.Ordinal))
        {
            return null;
        }

        string id = parts[1]["/trace/".Length..].Split('?', '#')[0];
        return _servedTraceFiles.TryGetValue(id, out string? path) ? path : null;
    }

    private static async Task WriteHttpResponse(Stream stream, string status, string contentType, byte[] body)
    {
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(body);
    }

    private static bool TryParseLong(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private enum TraceFormat
    {
        Speedscope,
        NetTrace,
    }

    private enum TraceProfile
    {
        CpuSampling,
        GcVerbose,
        GcCollect,
    }

    private enum TraceToolMode
    {
        None,
        LocalTool,
        PathCommand,
    }
}
