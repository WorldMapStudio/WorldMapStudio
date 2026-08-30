#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Transitions the editor through a full unload/reload cycle: waits for background work to quiesce,
/// drops every <see cref="IWorldParticipant"/>'s state, then reads it back fresh — off the frames
/// <see cref="Editor.Update"/> would otherwise be drawing, so nothing runs against half-torn-down
/// state. Hands control back to the same <see cref="Editor"/> instance once done, so window layout
/// and camera position survive; see <see cref="ScriptingSystem.Startup"/> for why that is safe to
/// re-enter.
///
/// Constructed by <see cref="Editor.Update"/> when <see cref="EditorContext.PendingReloadReason"/> is
/// set — by an aborted edit session today, by a batch operation once <c>WorldOperations</c> lands.
/// </summary>
public sealed class WorldReload : IScene
{
    private static readonly TimeSpan QuiesceTimeout = TimeSpan.FromSeconds(10.0);

    private enum Stage
    {
        Quiescing,
        Loading,
        Ready,
    }

    private readonly EditorContext _context;
    private readonly Editor _editor;
    private readonly Stopwatch _quiesceClock = Stopwatch.StartNew();

    private Stage _stage = Stage.Quiescing;
    private volatile string _step = "Waiting for background work to finish";
    private volatile bool _loadFinished;
    private volatile string? _error;
    private bool _forced;

    public WorldReload(EditorContext context, Editor editor)
    {
        _context = context;
        _editor = editor;
    }

    public void Start()
    {
        _context.ClearPendingReload();
        _context.BeginReload();
    }

    public IScene? Update()
    {
        if (_stage == Stage.Quiescing)
        {
            UpdateQuiescing();
        }

        Draw();

        if (_stage != Stage.Ready)
        {
            return this;
        }

        _context.EndReload();
        return _editor;
    }

    private void UpdateQuiescing()
    {
        // Nothing else drains these while this scene owns the frame: Editor.Update() is what
        // normally does, via ViewportWindow (streaming) and ImageSystem (residency), and it does not
        // run once a reload has taken over. Without this, a scan or chunk load already in flight the
        // instant the reload started would report busy forever — nothing would ever notice it landed
        // — and every reload would sit out the full quiesce timeout below.
        _context.Streaming.PumpCompletion();
        _context.Images.Residency.PumpCompletion();

        if (_context.Lifecycle.IsBusy && _quiesceClock.Elapsed < QuiesceTimeout)
        {
            return;
        }

        if (_context.Lifecycle.IsBusy)
        {
            GD.PushError("[World] Reload timed out waiting for background work to finish; unloading anyway.");
        }

        // Main thread, one pass: every participant's UnloadWorld runs synchronously here. It replaces
        // per-frame teardown (SyncRepresentations et al.), which this scene draws no frames for.
        IReadOnlyList<string> report = _context.Lifecycle.Unload();
        foreach (string leftover in report)
        {
            GD.PushWarning($"[World] {leftover}");
        }

        _stage = Stage.Loading;
        _step = "Preparing";
        _ = Task.Run(() =>
        {
            try
            {
                _context.Lifecycle.Load(step => _step = step);
                _loadFinished = true;
            }
            catch (Exception e)
            {
                _error = e.Message;
                GD.PushError($"[World] Reload failed: {e}");
            }
        });
    }

    private void Draw()
    {
        if (_stage == Stage.Loading && (_loadFinished || _forced))
        {
            _stage = Stage.Ready;
            return;
        }

        ImGuiEx.FullScreen("Reloading", ImGuiWindowFlags.None, () =>
        {
            ImGuiEx.Center(320, 60, 15, center =>
            {
                center.Label("Reloading the world");

                if (_error == null)
                {
                    // No real fraction to report while a phase runs, so animate a marquee for liveness —
                    // mirrors LoadingScreen, which this mirrors in every other respect too.
                    float fraction = (float)((ImGui.GetTime() * 0.6) % 1.0);
                    center.ProgressBar(fraction, $"{_step}…");
                }
                else
                {
                    center.LabelColored(_error, new System.Numerics.Vector4(0.9f, 0.4f, 0.4f, 1f));
                    center.Button("Continue Anyway", () => _forced = true);
                }
            });
        });
    }
}
