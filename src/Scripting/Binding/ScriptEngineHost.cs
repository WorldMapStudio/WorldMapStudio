using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace WorldMapStudio;

/// <summary>One evaluation's outcome, success and failure kept separate rather than folded into a
/// prefixed display string — what a structured caller (the HTTP endpoint) needs to build a proper
/// response instead of string-sniffing for an "Error:" prefix.</summary>
public readonly record struct ScriptResult(bool Success, string Output);

/// <summary>
/// Wraps the shared Jint engine every script call (console, and later the HTTP endpoint) runs
/// against — one instance per editor session, so state persists across calls the way Blender's
/// console and script text-blocks share one Python interpreter (see ScriptingPlan.md, decision #8).
///
/// The engine never gets raw CLR access. Two mechanisms enforce that, for two different shapes of
/// object: <see cref="TypeResolver.MemberFilter"/>, set once here to <see cref="ScriptReflection.IsVisible"/>,
/// filters every plain bound object (modules, and anything without special handling) down to
/// [ScriptProperty]/[ScriptFunction] members automatically; <see cref="EntityProxyHandler"/>, installed
/// below via <c>WrapObjectHandler</c>, does the equivalent for <see cref="ScriptEntityHandle"/>
/// specifically, because entities need direct property assignment to route through
/// <see cref="ScriptPropertyEditCommand"/> instead of a raw CLR setter — something MemberFilter alone
/// cannot express (see EntityProxyHandler's doc comment). Both read from the same
/// <see cref="ScriptReflection"/> metadata the .d.ts generator uses, so they can't disagree about
/// what's exposed.
/// </summary>
public sealed class ScriptEngineHost
{
    private readonly Engine _engine;

    // Requests from a non-main thread (the HTTP endpoint) — Jint's Engine is not thread-safe, so a
    // request can't call Evaluate directly from whatever thread accepted it. It enqueues here and
    // awaits a TaskCompletionSource that Update() completes once it dequeues and evaluates on the
    // main thread, the same shape WorkQueue's own background→main-thread jobs use.
    private readonly ConcurrentQueue<(string Code, TaskCompletionSource<ScriptResult> Completion)> _pending = new();

    public ScriptEngineHost(IEnumerable<IScriptModule> modules)
    {
        _engine = new Engine(options =>
        {
            options.SetTypeResolver(new TypeResolver { MemberFilter = ScriptReflection.IsVisible });
            options.TimeoutInterval(TimeSpan.FromSeconds(5));
            options.MaxStatements(2_000_000);

            // Entity handles get a hand-built Proxy instead of Jint's automatic CLR wrapping — see
            // EntityProxyHandler for why. Everything else must fall through to Jint's own default
            // wrapper *explicitly* — returning null here does not mean "use the default", it means
            // "this object wraps to nothing", which silently turns every other bound value (including
            // the "wms" global itself) into undefined. Confirmed against the real package: a handler
            // that unconditionally returns null breaks `engine.SetValue("wms", ...)` outright.
            options.Interop.WrapObjectHandler = (engine, target, type) => target is ScriptEntityHandle handle
                ? engine.Advanced.CreateProxy((ObjectInstance)engine.Evaluate("({})"), new EntityProxyHandler(engine, handle))
                : ObjectWrapper.Create(engine, target, type);
        });

        var wms = new Dictionary<string, object>();
        foreach (IScriptModule module in modules)
        {
            wms[module.Name] = module;
        }

        _engine.SetValue("wms", wms);
    }

    /// <summary>
    /// Drains completed async script work and any queued <see cref="EvaluateAsync"/> requests once.
    /// A [ScriptFunction] returning <c>Task</c>/<c>Task&lt;T&gt;</c> is automatically converted to a JS
    /// Promise by Jint itself (confirmed against the real package, including that a faulted Task
    /// correctly rejects it, catchable via JS <c>try</c>/<c>catch</c>) — this just needs calling once
    /// per frame from the main thread so a completed background Task's continuation actually resumes
    /// the awaiting script, without ever blocking the caller on it. Call from wherever already pumps
    /// other per-frame work (see <c>Editor.Update</c>), the same shape
    /// <see cref="WorkQueue.PumpMainThread"/> already uses for its own background→main-thread work.
    /// </summary>
    public void Update()
    {
        _engine.Advanced.ProcessTasks();

        while (_pending.TryDequeue(out (string Code, TaskCompletionSource<ScriptResult> Completion) item))
        {
            item.Completion.TrySetResult(Run(item.Code));
        }
    }

    /// <summary>
    /// Evaluates one snippet and returns a display string of the result, or an error message —
    /// never throws. This is the console's eval path: it runs synchronously on whatever thread calls
    /// it, which is only safe because the console always calls it from the main thread (ImGui runs
    /// there). A caller on any other thread must use <see cref="EvaluateAsync"/> instead.
    /// </summary>
    public string Evaluate(string code)
    {
        ScriptResult result = Run(code);
        return result.Success ? result.Output : $"Error: {result.Output}";
    }

    /// <summary>
    /// The thread-safe entry point: queues the snippet and returns a <see cref="Task"/> that
    /// completes once <see cref="Update"/> next dequeues and evaluates it on the main thread. Safe to
    /// call — and await — from any thread, which is exactly what the HTTP endpoint needs, since Jint's
    /// <see cref="Engine"/> itself is not thread-safe.
    /// </summary>
    public Task<ScriptResult> EvaluateAsync(string code)
    {
        // RunContinuationsAsynchronously matters: without it, whichever thread calls TrySetResult
        // (the main thread, inside Update()) would run the awaiter's continuation inline — meaning an
        // HTTP handler's post-await code (writing the response) would run on the Godot main thread.
        var completion = new TaskCompletionSource<ScriptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((code, completion));
        return completion.Task;
    }

    private ScriptResult Run(string code)
    {
        try
        {
            JsValue result = _engine.Evaluate(code);
            return new ScriptResult(true, result.IsUndefined() ? "undefined" : result.ToString());
        }
        catch (JavaScriptException e)
        {
            return new ScriptResult(false, e.Message);
        }
        catch (Exception e)
        {
            return new ScriptResult(false, $"{e.GetType().Name}: {e.Message}");
        }
    }
}
