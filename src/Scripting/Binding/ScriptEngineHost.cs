using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Jint;
using Jint.Constraints;
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
    /// <summary>
    /// How long a script's promise may stay unsettled before <see cref="EvaluateAsync"/> gives up on
    /// it. <c>TimeoutInterval</c> below bounds only synchronous execution; a script that awaits
    /// something that never resolves would otherwise hold its HTTP connection open forever.
    /// </summary>
    private static readonly TimeSpan SettlementTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Bounds one uninterrupted stretch of script. Jint resets its own time limit only when an
    /// <c>Evaluate</c> begins, so a script that awaited for longer than the limit was killed the moment
    /// it resumed — the clock was still running from its original call. This one is also reset by
    /// <see cref="Update"/> before it resumes awaiting scripts, so the limit applies per frame of work.
    /// </summary>
    private sealed class RunBudget(TimeSpan limit) : Constraint
    {
        private readonly long _ticks = (long)(limit.TotalSeconds * Stopwatch.Frequency);

        private long _deadline;

        public override void Reset() => _deadline = Stopwatch.GetTimestamp() + _ticks;

        public override void Check()
        {
            if (Stopwatch.GetTimestamp() > _deadline)
            {
                throw new TimeoutException();
            }
        }
    }

    private readonly Engine _engine;

    private readonly RunBudget _budget;

    // Requests from a non-main thread (the HTTP endpoint) — Jint's Engine is not thread-safe, so a
    // request can't call Evaluate directly from whatever thread accepted it. It enqueues here and
    // awaits a TaskCompletionSource that Update() completes once it dequeues and evaluates on the
    // main thread, the same shape WorkQueue's own background→main-thread jobs use.
    private readonly ConcurrentQueue<(string Code, TaskCompletionSource<ScriptResult> Completion)> _pending = new();

    // Requests whose script returned a promise. Their completion waits for the promise to settle,
    // which happens in some later Update(); the deadline is what stops one that never settles from
    // pinning a caller (and its HTTP connection) indefinitely.
    private readonly List<(TaskCompletionSource<ScriptResult> Completion, DateTime Deadline)> _awaiting = new();

    /// <param name="timeout">How long one stretch of synchronous script may run. Five seconds when omitted.</param>
    public ScriptEngineHost(IEnumerable<IScriptModule> modules, TimeSpan? timeout = null)
    {
        _budget = new RunBudget(timeout ?? TimeSpan.FromSeconds(5));
        _engine = new Engine(options =>
        {
            options.SetTypeResolver(new TypeResolver { MemberFilter = ScriptReflection.IsVisible });
            options.Constraint(_budget);
            options.MaxStatements(2_000_000);

            // Without this, a [ScriptFunction] returning Task/Task<T> crosses into JS as a wrapped CLR
            // object rather than a Promise — so `await wms.time.Wait(500)` returns the Task itself
            // immediately instead of waiting, and a faulted Task resolves as success instead of
            // throwing. Verified against the real package both ways: off, `await`ing LoadAsync() yields
            // "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1[...]" and
            // a throwing FailAsync() is never caught; on, they yield the value and reject properly.
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;

            // Entity handles get a hand-built Proxy instead of Jint's automatic CLR wrapping — see
            // EntityProxyHandler for why. Everything else must fall through to Jint's own default
            // wrapper *explicitly* — returning null here does not mean "use the default", it means
            // "this object wraps to nothing", which silently turns every other bound value (including
            // the "wms" global itself) into undefined. Confirmed against the real package: a handler
            // that unconditionally returns null breaks `engine.SetValue("wms", ...)` outright.
            // The proxy target itself must carry the CLR handle, not an empty JS object: Jint's
            // argument binder recovers a JS value's CLR representation via JsValue.ToObject(), and
            // JsProxy.ToObject() forwards to its *target's* ToObject() (ObjectWrapper.ToObject()
            // returns the wrapped instance) rather than going through the proxy's get trap. An empty
            // {} target made that ToObject() call yield a generic object with no relation to the
            // handle, so a handle returned from one [ScriptFunction] and passed as an argument into
            // another — the exact case ViewportScriptApi.Focus/SceneScriptApi.Delete rely on — failed
            // overload resolution with "No public methods with the specified arguments were found."
            // Verified against the real package: swapping in an ObjectWrapper-backed target fixes the
            // round trip without changing any Get/Set/method-call behavior, since EntityProxyHandler's
            // traps still intercept every access before the target is ever consulted.
            options.Interop.WrapObjectHandler = (engine, target, type) => target is ScriptEntityHandle handle
                ? engine.Advanced.CreateProxy(
                    (ObjectInstance)ObjectWrapper.Create(engine, handle, typeof(ScriptEntityHandle)),
                    new EntityProxyHandler(engine, handle))
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
    /// Drives every kind of pending script work once: resumes scripts whose awaited <c>Task</c> has
    /// completed, evaluates newly queued <see cref="EvaluateAsync"/> requests, and times out any whose
    /// promise never settled. Call once per frame from the main thread (see <c>Editor.Update</c>) —
    /// the same shape <see cref="WorkQueue.PumpMainThread"/> uses for its own background→main-thread
    /// work — and never block on any of it.
    /// </summary>
    public void Update()
    {
        _budget.Reset();
        _engine.Advanced.ProcessTasks();

        while (_pending.TryDequeue(out (string Code, TaskCompletionSource<ScriptResult> Completion) item))
        {
            Submit(item.Code, item.Completion);
        }

        ExpireUnsettled();
    }

    /// <summary>
    /// Evaluates one snippet synchronously and returns a display string of the result, or an error
    /// message — never throws. Safe only from the main thread, because Jint's <see cref="Engine"/> is
    /// not thread-safe; the console calls it from there (ImGui runs on the main thread).
    ///
    /// Synchronous means it cannot wait: a script that returns a promise (anything using <c>await</c>)
    /// renders as <c>[object Promise]</c>, because settling one needs later <see cref="Update"/> calls
    /// that cannot happen while this is on the stack. Use <see cref="EvaluateAsync"/> to get the
    /// settled value.
    /// </summary>
    public string Evaluate(string code)
    {
        ScriptResult result = Run(code, out _);
        return result.Success ? result.Output : $"Error: {result.Output}";
    }

    /// <summary>
    /// The thread-safe entry point, and the only one that can report the result of an asynchronous
    /// script: queues the snippet and returns a <see cref="Task"/> that completes once
    /// <see cref="Update"/> has evaluated it on the main thread <em>and</em> — if it returned a
    /// promise — that promise has settled. Safe to call, and await, from any thread.
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

    // Main thread. Completes the request now for a plain result, or hands it to the promise once the
    // script turned out to be asynchronous.
    private void Submit(string code, TaskCompletionSource<ScriptResult> completion)
    {
        ScriptResult result = Run(code, out JsValue? promise);
        if (promise is null)
        {
            completion.TrySetResult(result);
            return;
        }

        if (!TryContinueWith(promise, completion))
        {
            // Falling back to the promise's own display string is still better than hanging: the
            // caller gets *something* and the request ends.
            completion.TrySetResult(result);
            return;
        }

        _awaiting.Add((completion, DateTime.UtcNow + SettlementTimeout));
    }

    // Attaches CLR continuations through the promise's own `then`, so settlement is observed by the
    // same ProcessTasks() pump that drives everything else rather than by blocking. (Jint's
    // UnwrapIfPromise does the blocking version, with a ten-second default — verified against the real
    // package, and exactly what must not happen on the Godot main thread.)
    private bool TryContinueWith(JsValue promise, TaskCompletionSource<ScriptResult> completion)
    {
        try
        {
            JsValue then = promise.AsObject().Get("then");
            Func<JsValue, JsValue> onFulfilled = value =>
            {
                completion.TrySetResult(new ScriptResult(true, Display(value)));
                return JsValue.Undefined;
            };
            Func<JsValue, JsValue> onRejected = reason =>
            {
                completion.TrySetResult(new ScriptResult(false, reason.ToString()));
                return JsValue.Undefined;
            };

            _engine.Invoke(then, promise, new object[] { onFulfilled, onRejected });
            return true;
        }
        catch (Exception e)
        {
            // Fully qualified: `using Godot` would make Godot.Engine clash with Jint.Engine here.
            Godot.GD.PushError($"[Scripting] Could not observe a script's promise: {e.Message}");
            return false;
        }
    }

    private void ExpireUnsettled()
    {
        if (_awaiting.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        for (int i = _awaiting.Count - 1; i >= 0; i--)
        {
            (TaskCompletionSource<ScriptResult> completion, DateTime deadline) = _awaiting[i];

            // Already settled by its `then` handler, or past its deadline. Either way stop tracking
            // it; TrySetResult means a late settlement is harmlessly ignored.
            if (completion.Task.IsCompleted)
            {
                _awaiting.RemoveAt(i);
            }
            else if (now >= deadline)
            {
                completion.TrySetResult(new ScriptResult(
                    false, $"The script's promise did not settle within {SettlementTimeout.TotalSeconds:0}s."));
                _awaiting.RemoveAt(i);
            }
        }
    }

    // Evaluates and reports the outcome. On success, a promise result is handed back separately so
    // the caller can decide whether it is able to wait for it.
    private ScriptResult Run(string code, out JsValue? promise)
    {
        promise = null;
        try
        {
            JsValue result = _engine.Evaluate(code);
            if (result.IsPromise())
            {
                promise = result;
            }

            return new ScriptResult(true, Display(result));
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

    private static string Display(JsValue value) => value.IsUndefined() ? "undefined" : value.ToString();
}
