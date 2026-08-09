using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Hosts the editor's JS-scriptable surface. Script modules self-register with
/// [Subsystem(nameof(ScriptingSystem))] and are constructed by the generated
/// InitializeSubsystems(), exactly like <see cref="Storage"/>s under <see cref="DatabaseSystem"/>. A
/// plain member of <see cref="EditorContext"/> (the core spine, not an extension point itself), but
/// itself a host, following the same pattern <see cref="DatabaseSystem"/> and <see cref="ToolSystem"/>
/// already establish.
/// </summary>
public sealed partial class ScriptingSystem : ISubsystemHost
{
    public EditorContext Context { get; }

    public IEnumerable<IScriptModule> Modules => Subsystems.Cast<IScriptModule>();

    /// <summary>The running engine, or null until <see cref="Startup"/> has run.</summary>
    public ScriptEngineHost? Engine { get; private set; }

    private EventsScriptApi? _events;

    public ScriptingSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    /// <summary>
    /// Builds the JS engine's bindings and (re)writes the type declarations. Deferred out of the
    /// constructor — like <see cref="DatabaseSystem.Startup"/> — so every module has already been
    /// constructed first, and so it runs alongside the rest of <c>Editor.Start()</c>'s startup work
    /// rather than the subsystem-construction phase.
    /// </summary>
    public void Startup()
    {
        Engine = new ScriptEngineHost(Modules);
        _events = Modules.OfType<EventsScriptApi>().FirstOrDefault();
        ScriptTypeDeclarationWriter.Write(Modules);
    }

    /// <summary>
    /// Drains pending async script work and checks for event changes. Call once per frame from the
    /// main thread (see <c>Editor.Update</c>) — never before <see cref="Startup"/> has run.
    /// </summary>
    public void Update()
    {
        Engine?.Update();
        _events?.Update();
    }
}
