using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Starts the server (if not already running) and waits until it accepts connections.
    ///
    /// If the host:port is already accepting connections before we've launched anything, that can only
    /// be a leftover server from a previous session (Godot crashing or being killed without running
    /// <see cref="Stop"/> orphans the process). Left alone, our own launch would fail to bind the port
    /// and exit, while <see cref="WaitUntilReady"/> keeps polling the socket and happily reports success
    /// once it sees the *old* process answering — silently handing the caller a connection to stale data
    /// instead of the fresh server it asked for. So when that's detected,
    /// <paramref name="confirmKillStray"/> (if given) is asked whether to kill the stray process and
    /// retry; declining or having no callback aborts the start instead of connecting through.
    /// </summary>
    public bool Start(TimeSpan timeout, Func<string, bool>? confirmKillStray = null)
    {
        if (IsRunning)
        {
            return true;
        }

        Directory.CreateDirectory(_dataDir);

        if (CanConnect() && !ReclaimPort(confirmKillStray))
        {
            return false;
        }

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

    /// <summary>
    /// Something is already listening on <see cref="_host"/>:<see cref="_port"/>. Looks for a leftover
    /// <see cref="_executable"/> process to blame, asks the caller whether to kill it, and if so waits
    /// for the port to free up. Only "yes, and it worked" returns true — anything else (no candidate
    /// process, no callback, declined, or still occupied after killing) leaves the port alone and fails.
    /// </summary>
    private bool ReclaimPort(Func<string, bool>? confirmKillStray)
    {
        Process[] stray = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_executable));
        if (stray.Length == 0)
        {
            GD.PushError($"[Dolt] {_host}:{_port} is already in use by another process (not a leftover '{_executable}'); refusing to start.");
            return false;
        }

        string pids = string.Join(", ", stray.Select(p => p.Id));
        string message = $"A previous '{_executable}' process (PID {pids}) is still holding {_host}:{_port}, likely left running from an earlier session. Kill it and retry?";

        if (confirmKillStray == null || !confirmKillStray(message))
        {
            GD.PushError($"[Dolt] {_host}:{_port} is occupied by a leftover '{_executable}' process (PID {pids}); refusing to start.");
            return false;
        }

        foreach (Process p in stray)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
            }
            catch (Exception e)
            {
                GD.PushError($"[Dolt] Failed to kill stray process {p.Id}: {e.Message}");
            }
            finally
            {
                p.Dispose();
            }
        }

        if (!WaitUntilPortFree(TimeSpan.FromSeconds(5)))
        {
            GD.PushError($"[Dolt] {_host}:{_port} is still occupied after killing stray '{_executable}' process(es).");
            return false;
        }

        return true;
    }

    private bool WaitUntilPortFree(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!CanConnect())
            {
                return true;
            }

            Thread.Sleep(150);
        }

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
