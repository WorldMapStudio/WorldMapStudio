using System.Linq;

namespace WorldMapStudio;

/// <summary>The current editor selection, exposed to JS as <c>wms.selection</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class SelectionScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "selection";

    public SelectionScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>The currently selected entities.</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] Get() =>
        _context.Selection.Selected.OfType<Entity>()
            .Select(entity => new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity))
            .ToArray();
}
