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
[SubsystemHost(typeof(IScriptModule))]
public sealed partial class ScriptingSystem : ISubsystemHost, IFrameParticipant
{
    public EditorContext Context { get; }

    public IEnumerable<IScriptModule> Modules => Subsystems;

    /// <summary>The running engine, or null until <see cref="Startup"/> has run.</summary>
    public ScriptEngineHost? Engine { get; private set; }

    /// <summary>The local HTTP endpoint fronting <see cref="Engine"/>, or null until <see cref="Startup"/> has run.</summary>
    public ScriptHttpServer? Http { get; private set; }

    private EventsScriptApi? _events;

    public ScriptingSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
    }

    /// <summary>
    /// Builds the JS engine's bindings, (re)writes the type declarations, and starts the local HTTP
    /// endpoint. Deferred out of the constructor — like <see cref="DatabaseSystem.Startup"/> — so
    /// every module has already been constructed first, and so it runs alongside the rest of
    /// <c>Editor.Start()</c>'s startup work rather than the subsystem-construction phase.
    ///
    /// Idempotent: <see cref="Editor.Start"/> runs again every time a <see cref="WorldReload"/> hands
    /// control back to the same <see cref="Editor"/> instance, and a second engine (or a second
    /// attempt at the same HTTP port) is not what that should mean.
    /// </summary>
    public void Startup()
    {
        if (Engine != null)
        {
            return;
        }

        Engine = new ScriptEngineHost(Modules);
        _events = Modules.OfType<EventsScriptApi>().FirstOrDefault();
        ScriptTypeDeclarationWriter.Write(Modules);

        Http = new ScriptHttpServer(
            Engine,
            ResolvePort(),
            () => !Context.IsReloading && Context.PendingReloadReason == null,
            Context.Project.Name);
        Http.Start();
    }

    private const string PortFlag = "--script-port";

    private const int DefaultPort = 8765;

    private static int ResolvePort()
    {
        string? value = CommandLine.Value(PortFlag);
        if (value == null)
        {
            return DefaultPort;
        }

        if (int.TryParse(value, out int port) && port is > 0 and <= 65535)
        {
            return port;
        }

        Godot.GD.PushError($"[Scripting] Ignoring {PortFlag} '{value}': expected a port from 1 to 65535. Using {DefaultPort}.");
        return DefaultPort;
    }

    public float TickPriority => 4f;

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
