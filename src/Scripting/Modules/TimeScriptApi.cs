using System;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// A small real-world proof of the async bridge (see ScriptingPlan.md's Phase 10 notes), exposed to
/// JS as <c>wms.time</c>: <c>await wms.time.Wait(500)</c> pauses a script without blocking the editor,
/// useful for scripted sequences (move the camera, wait, move again) — a genuine utility, not just a
/// test fixture.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class TimeScriptApi : IScriptModule
{
    public string Name => "time";

    public float Priority => 0f;

    public TimeScriptApi(ScriptingSystem system)
    {
    }

    /// <summary>Resolves after the given delay. Never blocks the calling thread.</summary>
    [ScriptFunction]
    public Task Wait(int milliseconds) => Task.Delay(Math.Max(0, milliseconds));
}
