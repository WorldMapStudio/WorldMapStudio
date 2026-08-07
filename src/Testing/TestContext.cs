#nullable enable
using System.Collections.Generic;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Handed to a test body. Gives it access to the live editor (via <see cref="EditorRoot"/>), a place
/// to record diagnostic <see cref="Log"/> lines that show up in the result details, cooperative
/// cancellation, and the ability to hop threads mid-test through the underlying <see cref="WorkContext"/>.
/// </summary>
public sealed class TestContext
{
    private readonly WorkContext _work;
    private readonly List<string> _logs;

    internal TestContext(WorkContext work, Node? editorRoot, List<string> logs)
    {
        _work = work;
        EditorRoot = editorRoot;
        _logs = logs;
    }

    /// <summary>
    /// The editor's root node (the <c>WorldMapStudio</c> scene root), or null if the runner was
    /// created without one. Cast it to reach editor services; only touch it from the main thread.
    /// </summary>
    public Node? EditorRoot { get; }

    /// <summary>True once the run has been asked to stop; long tests should check this and bail out.</summary>
    public bool IsCancellationRequested => _work.IsCancellationRequested;

    /// <summary>Records a diagnostic line, shown under the test's result in the Test Runner window.</summary>
    public void Log(string message) => _logs.Add(message ?? "");

    /// <summary>Throws to abort the test if the run was cancelled.</summary>
    public void ThrowIfCancellationRequested() => _work.ThrowIfCancellationRequested();

    /// <summary>Resumes the test on the main thread (within the per-frame budget).</summary>
    public ThreadSwitchAwaitable SwitchToMain() => _work.SwitchToMain();

    /// <summary>Resumes the test on a background worker thread.</summary>
    public ThreadSwitchAwaitable SwitchToBackground() => _work.SwitchToBackground();

    /// <summary>Yields and re-queues on the current thread so other work can interleave.</summary>
    public ThreadSwitchAwaitable Yield() => _work.Yield();
}
