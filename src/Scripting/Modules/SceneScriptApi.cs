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

    /// <summary>Every loaded scene entity in view, optionally filtered by component type id.</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] All(string? componentType = null)
    {
        IEnumerable<SceneEntity> entities = _context.Scene.InView;
        if (!string.IsNullOrEmpty(componentType))
        {
            entities = entities.Where(entity => entity.Components.Any(component => component.TypeId == componentType));
        }

        return entities.Select(ToHandle).ToArray();
    }

    /// <summary>The loaded scene entity with this id, or null if none is loaded.</summary>
    [ScriptFunction]
    public ScriptEntityHandle? ById(long id)
    {
        SceneEntity? entity = _context.Scene.InView.FirstOrDefault(e => e.Id.Value == id);
        return entity is null ? null : ToHandle(entity);
    }

    /// <summary>Creates a generic scene entity in the current map and optionally adds one component by type id.</summary>
    [ScriptFunction]
    public ScriptEntityHandle Create(string? componentType = null)
    {
        var entity = new SceneEntity { Map = _context.Maps.CurrentMap };
        if (!string.IsNullOrEmpty(componentType))
        {
            entity.AddComponent(CreateComponent(componentType));
            entity.Name = DefaultName(componentType);
        }

        _context.Scene.Add(entity);
        _context.EditSessions.Record(new CreateEntityCommand(_context.Scene, entity));
        return ToHandle(entity);
    }

    [ScriptFunction]
    public void AddComponent(ScriptEntityHandle handle, string componentType)
    {
        SceneEntity entity = RequireSceneEntity(handle);
        if (entity.Components.Any(component => component.TypeId == componentType))
        {
            return;
        }

        SceneComponent component = CreateComponent(componentType);
        var command = new AddComponentCommand(entity, component);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    [ScriptFunction]
    public void RemoveComponent(ScriptEntityHandle handle, string componentType)
    {
        SceneEntity entity = RequireSceneEntity(handle);
        SceneComponent? component = entity.Components.FirstOrDefault(component => component.TypeId == componentType);
        if (component == null)
        {
            return;
        }

        var command = new RemoveComponentCommand(entity, component);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    [ScriptFunction]
    public string[] Components(ScriptEntityHandle handle) =>
        RequireSceneEntity(handle).Components.Select(component => component.TypeId).ToArray();

    /// <summary>Deletes the entity a handle refers to, undoably.</summary>
    [ScriptFunction]
    public void Delete(ScriptEntityHandle handle)
    {
        SceneEntity entity = RequireSceneEntity(handle);
        var command = new DeleteEntityCommand(_context.Scene, entity);
        command.Apply();
        _context.EditSessions.Record(command);
        _context.Selection.Remove(entity);
    }

    private ScriptEntityHandle ToHandle(SceneEntity entity) =>
        new(_context.Scene, _context.Catalog, _context.EditSessions, entity);

    private static SceneEntity RequireSceneEntity(ScriptEntityHandle handle) =>
        handle.Resolve() is SceneEntity entity
            ? entity
            : throw new InvalidOperationException("That handle does not refer to a scene entity.");

    private static SceneComponent CreateComponent(string typeId) => typeId switch
    {
        "marker" => new MarkerComponent(),
        "landscape-stamp" => new StampComponent(),
        "drawing-target" => new DrawingTargetComponent(),
        "landscape-material-bind" => new LandscapeMaterialBindComponent(),
        _ => throw new InvalidOperationException($"No scene component type named '{typeId}'."),
    };

    private static string DefaultName(string typeId) => typeId switch
    {
        "marker" => "Empty",
        "landscape-stamp" => "Stamp",
        "drawing-target" => "Drawing Target",
        "landscape-material-bind" => "Landscape Material Bind",
        _ => "Entity",
    };
}
