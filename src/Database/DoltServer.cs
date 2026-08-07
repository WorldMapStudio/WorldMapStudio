using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Launches and owns a <c>dolt sql-server</c> process for a data directory, so the editor can manage
/// its own database instance. Polls until the server accepts connections, and kills the process
/// (and any children) on <see cref="Stop"/> or process exit so none is orphaned.
/// </summary>
public sealed class DoltServer
{
    private readonly string _dataDir;
    private readonly string _host;
    private readonly int _port;
    private readonly string _executable;
    private Process? _process;

    public DoltServer(string dataDir, string host, int port, string executable = "dolt")
    {
        _dataDir = dataDir;
        _host = host;
        _port = port;
        _executable = executable;
    }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Starts the server (if not already running) and waits until it accepts connections.</summary>
    public bool Start(TimeSpan timeout)
    {
        if (IsRunning)
        {
            return true;
        }

        Directory.CreateDirectory(_dataDir);

        var startInfo = new ProcessStartInfo(_executable, $"sql-server --data-dir \"{_dataDir}\" -H {_host} -P {_port}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Exception e)
        {
            GD.PushError($"[Dolt] Failed to launch '{_executable}': {e.Message}");
            return false;
        }

        if (_process == null)
        {
            return false;
        }

        // Drain the pipes so their buffers can't fill and stall the server.
        _process.OutputDataReceived += static (_, _) => { };
        _process.ErrorDataReceived += static (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        return WaitUntilReady(timeout);
    }

    public void Stop()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        if (_process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Dolt] Failed to stop server: {e.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Stop();

    private bool WaitUntilReady(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                GD.PushError($"[Dolt] Server exited early (code {_process.ExitCode}).");
                return false;
            }

            if (CanConnect())
            {
                return true;
            }

            Thread.Sleep(150);
        }

        GD.PushError($"[Dolt] Server did not become ready within {timeout.TotalSeconds:0}s.");
        return false;
    }

    private bool CanConnect()
    {
        try
        {
            using var client = new TcpClient();
            IAsyncResult connect = client.BeginConnect(_host, _port, null, null);
            if (connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250)) && client.Connected)
            {
                client.EndConnect(connect);
                return true;
            }
        }
        catch
        {
            // Not accepting connections yet.
        }

        return false;
    }
}
