using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// A local-only HTTP endpoint fronting the shared <see cref="ScriptEngineHost"/> — the editor-side
/// half of the MCP-readiness this scripting system was built for (see ScriptingDesign.md's "primary
/// motivation" note). Speaks plain HTTP, not MCP itself: a separate adapter process is expected to
/// translate MCP tool calls into requests here, the same way <c>ghidra-mcp</c> fronts Ghidra's own
/// local HTTP endpoint elsewhere in this workspace (see the top-level <c>CLAUDE.md</c>).
///
/// Bound to <c>127.0.0.1</c> only — a local, trusted-caller surface, not a public API. Guardrails are
/// the engine's own (statement/timeout limits from <see cref="ScriptEngineHost"/>), not
/// authentication. Kept out of <c>src/Scripting/</c> itself so the core binder stays
/// transport-agnostic — this and the console window are two equally-privileged callers of the same
/// <see cref="ScriptEngineHost"/>.
///
/// Runs on a background accept loop for the process's lifetime once started; not explicitly stopped
/// on editor shutdown.
/// </summary>
public sealed class ScriptHttpServer
{
    private readonly HttpListener _listener = new();
    private readonly ScriptEngineHost _engine;
    private readonly Func<bool> _isReady;
    private readonly string _project;

    public int Port { get; }

    /// <param name="isReady">Reported by <c>/health</c> as <c>ready</c>; a caller polling for the editor
    /// to come up waits on it. Always true when omitted.</param>
    /// <param name="project">Reported by <c>/health</c>, so a caller can tell which editor answered.</param>
    public ScriptHttpServer(ScriptEngineHost engine, int port = 8765, Func<bool>? isReady = null, string project = "")
    {
        _engine = engine;
        _isReady = isReady ?? (() => true);
        _project = project;
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>Starts listening. Logs and gives up (rather than throwing) if the port is unavailable, e.g. another editor instance already bound it.</summary>
    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (Exception e)
        {
            GD.PushError($"[Scripting] HTTP endpoint failed to start on port {Port}: {e.Message}");
            return;
        }

        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return; // Stop() was called mid-accept; a clean shutdown, not a real failure.
            }

            _ = Handle(context); // each request is independent; one failing must not stop the loop
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "";
            switch (context.Request.HttpMethod, path)
            {
                case ("GET", "/health"):
                    await WriteJson(context, 200, JsonSerializer.Serialize(new
                    {
                        ok = true,
                        ready = _isReady(),
                        project = _project,
                        pid = System.Environment.ProcessId,
                    })).ConfigureAwait(false);
                    break;

                case ("GET", "/types"):
                    await HandleTypes(context).ConfigureAwait(false);
                    break;

                case ("POST", "/run"):
                    await HandleRun(context).ConfigureAwait(false);
                    break;

                default:
                    await WriteJson(context, 404, "{\"ok\":false,\"error\":\"not found\"}").ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[Scripting] HTTP request to {context.Request.Url} failed: {e}");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // The connection may already be gone; nothing more to do.
            }
        }
    }

    private async Task HandleRun(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        string code = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Never touches Jint's Engine on this thread — queues onto ScriptEngineHost.Update()'s
        // main-thread drain and awaits the result, exactly like a script's own `await` does.
        ScriptResult result = await _engine.EvaluateAsync(code).ConfigureAwait(false);

        string json = result.Success
            ? JsonSerializer.Serialize(new { ok = true, result = result.Output })
            : JsonSerializer.Serialize(new { ok = false, error = result.Output });

        await WriteJson(context, 200, json).ConfigureAwait(false);
    }

    private static async Task HandleTypes(HttpListenerContext context)
    {
        string path = ProjectSettings.GlobalizePath("res://.types/wms.d.ts");
        string text = File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : "";
        await WriteText(context, 200, text, "text/plain; charset=utf-8").ConfigureAwait(false);
    }

    private static Task WriteJson(HttpListenerContext context, int status, string json) =>
        WriteText(context, status, json, "application/json; charset=utf-8");

    private static async Task WriteText(HttpListenerContext context, int status, string text, string contentType)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }
}
