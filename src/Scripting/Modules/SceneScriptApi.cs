using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>Scene entity queries and edits, exposed to JS as <c>wms.scene</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class SceneScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "scene";

    public float Priority => 0f;

    public SceneScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>Every loaded scene entity in view, optionally filtered to one CLR type by name (e.g. "EmptyEntity").</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] All(string? typeName = null)
    {
        IEnumerable<SceneEntity> entities = _context.Scene.InView;
        if (!string.IsNullOrEmpty(typeName))
        {
            entities = entities.Where(entity => entity.GetType().Name == typeName);
        }

        return entities.Select(ToHandle).ToArray();
    }

    /// <summary>The loaded scene entity with this id, or null if none is loaded (it may have streamed out).</summary>
    [ScriptFunction]
    public ScriptEntityHandle? ById(long id)
    {
        SceneEntity? entity = _context.Scene.InView.FirstOrDefault(e => e.Id.Value == id);
        return entity is null ? null : ToHandle(entity);
    }

    /// <summary>
    /// Creates a new entity of the given CLR type (e.g. "EmptyEntity") in the current map, records it
    /// into the active edit session, and returns a handle to it — the same create/commit flow "Scene →
    /// Add Empty" already uses (see SceneMenu.AddEmptyItem).
    /// </summary>
    [ScriptFunction]
    public ScriptEntityHandle Create(string typeName)
    {
        Type type = typeof(SceneEntity).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == typeName && !t.IsAbstract && typeof(SceneEntity).IsAssignableFrom(t))
            ?? throw new InvalidOperationException($"No SceneEntity type named '{typeName}'.");

        var entity = (SceneEntity)Activator.CreateInstance(type)!;
        entity.Map = _context.Maps.CurrentMap;

        _context.Scene.Add(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
        return ToHandle(entity);
    }

    /// <summary>Deletes the entity a handle refers to — undoable, and reaches the database on commit like any other deletion.</summary>
    [ScriptFunction]
    public void Delete(ScriptEntityHandle handle)
    {
        if (handle.Resolve() is not SceneEntity entity)
        {
            throw new InvalidOperationException("That handle does not refer to a scene entity.");
        }

        var command = new DeleteEntityCommand(_context.Scene, entity);
        command.Apply();
        _context.EditSessions.Record(command);
        _context.Selection.Remove(entity);
    }

    private ScriptEntityHandle ToHandle(SceneEntity entity) =>
        new(_context.Scene, _context.Catalog, _context.EditSessions, entity);
}
