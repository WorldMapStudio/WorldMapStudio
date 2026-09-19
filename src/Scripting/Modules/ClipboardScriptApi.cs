using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>The scene clipboard, exposed to JS as <c>wms.clipboard</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ClipboardScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "clipboard";

    public ClipboardScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Whether something has been copied.</summary>
    [ScriptProperty]
    public bool HasContent => _context.Clipboard.HasContent;

    /// <summary>Copies the entities, or the current selection when none are given.</summary>
    [ScriptFunction]
    public void Copy(ScriptEntityHandle[]? handles = null)
    {
        IEnumerable<SceneEntity> entities = handles == null
            ? _context.Selection.Selected.OfType<SceneEntity>()
            : handles.Select(handle => handle.Resolve() as SceneEntity
                ?? throw new InvalidOperationException("A handle does not refer to a scene entity."));
        _context.Clipboard.Copy(entities.ToList());
    }

    /// <summary>
    /// Pastes the copied entities so the bottom-centre of their combined bounds lands at world
    /// (x, y, z), as one undo step. Returns the pasted entities; with <paramref name="select"/> (the
    /// default) they also become the selection.
    /// </summary>
    [ScriptFunction]
    public ScriptEntityHandle[] Paste(double x, double y, double z, bool select = true)
    {
        (IReadOnlyList<SceneEntity> pasted, IEditCommand? command) = _context.Clipboard.PasteAt(
            _context.Scene, _context.Maps.CurrentMap, new Vector3((float)x, (float)y, (float)z));
        if (command == null)
        {
            return [];
        }

        command.Apply();
        _context.EditSessions.Record(command);

        if (select)
        {
            _context.Selection.Clear();
            foreach (SceneEntity entity in pasted)
            {
                _context.Selection.Add(entity);
            }
        }

        return pasted.Select(entity => new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity)).ToArray();
    }

    /// <summary>Forgets what was copied.</summary>
    [ScriptFunction]
    public void Clear() => _context.Clipboard.Clear();
}
