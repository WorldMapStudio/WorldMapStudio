using System;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Reads and switches the active editor tool, exposed to JS as <c>wms.tools</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ToolsScriptApi : IScriptModule
{
    private readonly ToolSystem _tools;

    public string Name => "tools";

    public float Priority => 0f;

    public ToolsScriptApi(ScriptingSystem system)
    {
        _tools = system.Context.Tools;
    }

    /// <summary>The active tool's name, or null before one has activated.</summary>
    [ScriptProperty]
    public string? Active => _tools.Active?.Name;

    [ScriptFunction]
    public string[] List() => _tools.Factories.Select(f => f.Name).ToArray();

    [ScriptFunction]
    public void Activate(string name)
    {
        IToolFactory factory = _tools.Factories.FirstOrDefault(f => f.Name == name)
            ?? throw new InvalidOperationException($"No tool named '{name}'.");

        if (!factory.CanActivate())
        {
            throw new InvalidOperationException($"Tool '{name}' cannot be activated right now.");
        }

        _tools.Activate(factory);
    }
}
