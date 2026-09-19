using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Event hooks, exposed to JS as <c>wms.events</c>: <c>wms.events.On("selectionChanged", fn)</c> /
/// <c>Off(name)</c>. Hand-wired to a small, fixed set of events (not a generic
/// attribute-driven mechanism — see ScriptingPlan.md's Phase 8/9 open gap on this) by polling
/// <see cref="SelectionSystem.Version"/>/<see cref="MapSystem.Version"/> once per frame via
/// <see cref="Update"/>, since nothing in this codebase raises real C# events for these changes —
/// everything here already follows a "bump a Version counter, let views poll it" convention.
///
/// <see cref="Off"/> removes every handler registered for a name, not a single specific callback —
/// matching a JS function value across the JS/CLR boundary reliably enough to support fine-grained
/// removal wasn't worth the risk to verify this pass. Document the coarser behavior rather than
/// pretend precision that isn't there.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class EventsScriptApi : IScriptModule
{
    private readonly SelectionSystem _selection;
    private readonly Func<int> _mapVersion;
    private readonly Dictionary<string, List<Action>> _handlers = new();

    private int _lastSelectionVersion;
    private int _lastMapVersion;

    public string Name => "events";

    public EventsScriptApi(ScriptingSystem system)
        : this(system.Context.Selection, () => system.Context.Maps.Version)
    {
    }

    // Takes the map version as a delegate rather than a MapSystem directly so this class is
    // unit-testable without constructing a real EditorContext (MapSystem's constructor needs one).
    internal EventsScriptApi(SelectionSystem selection, Func<int> mapVersion)
    {
        _selection = selection;
        _mapVersion = mapVersion;
        _lastSelectionVersion = selection.Version;
        _lastMapVersion = mapVersion();
    }

    [ScriptFunction]
    public void On(string eventName, Action callback)
    {
        if (!_handlers.TryGetValue(eventName, out List<Action>? list))
        {
            _handlers[eventName] = list = [];
        }

        list.Add(callback);
    }

    [ScriptFunction]
    public void Off(string eventName) => _handlers.Remove(eventName);

    /// <summary>Checks for changes and fires handlers. Call once per frame from the main thread.</summary>
    internal void Update()
    {
        if (_selection.Version != _lastSelectionVersion)
        {
            _lastSelectionVersion = _selection.Version;
            Fire("selectionChanged");
        }

        int mapVersion = _mapVersion();
        if (mapVersion != _lastMapVersion)
        {
            _lastMapVersion = mapVersion;
            Fire("mapChanged");
        }
    }

    private void Fire(string eventName)
    {
        if (!_handlers.TryGetValue(eventName, out List<Action>? list))
        {
            return;
        }

        // Snapshot first: a handler that calls On/Off for the same event must not corrupt this pass.
        foreach (Action callback in list.ToArray())
        {
            callback();
        }
    }
}
