using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace WorldMapStudio;

/// <summary>Scene entity queries and edits, exposed to JS as <c>wms.scene</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class SceneScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "scene";

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

    /// <summary>The entity's world position as [x, y, z].</summary>
    [ScriptFunction]
    public double[] GetPosition(ScriptEntityHandle handle)
    {
        Vector3 origin = RequireSceneEntity(handle).Transform.Origin;
        return [origin.X, origin.Y, origin.Z];
    }

    /// <summary>Moves the entity, keeping its current rotation/scale, undoably — the same command a gizmo drag applies.</summary>
    [ScriptFunction]
    public void SetPosition(ScriptEntityHandle handle, double x, double y, double z)
    {
        SceneEntity entity = RequireSceneEntity(handle);
        Transform3D before = entity.Transform;
        Transform3D after = new(before.Basis, new Vector3((float)x, (float)y, (float)z));
        var command = new TransformEntitiesCommand([entity], [before], [after]);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    /// <summary>Reads a <c>[ScriptProperty]</c> field of a named component attached to the entity.</summary>
    [ScriptFunction]
    public object? GetComponentField(ScriptEntityHandle handle, string componentType, string field)
    {
        SceneComponent component = RequireComponent(handle, componentType);
        return FindComponentProperty(component, field).GetValue(component);
    }

    /// <summary>Writes a <c>[ScriptProperty(Mutable = true)]</c> field of a named component, undoably.</summary>
    [ScriptFunction]
    public void SetComponentField(ScriptEntityHandle handle, string componentType, string field, object? value)
    {
        SceneComponent component = RequireComponent(handle, componentType);
        PropertyInfo property = FindComponentProperty(component, field);
        if (!ScriptReflection.IsMutable(property))
        {
            throw new InvalidOperationException($"'{field}' is not mutable from scripts.");
        }

        object? before = property.GetValue(component);
        object? after = ScriptEntityHandle.ConvertForClr(value, property.PropertyType);
        var command = new ScriptComponentFieldEditCommand(component, field,
            () => property.SetValue(component, after),
            () => property.SetValue(component, before));
        command.Apply();
        _context.EditSessions.Record(command);
    }

    /// <summary>Reads one element of an array-typed <c>[ScriptProperty]</c> field of a named component
    /// (e.g. a WoW light's <c>ParamIds</c> slots), which have no whole-property setter to go through
    /// <see cref="GetComponentField"/>/<see cref="SetComponentField"/> instead.</summary>
    [ScriptFunction]
    public object? GetComponentArrayField(ScriptEntityHandle handle, string componentType, string field, int index)
    {
        SceneComponent component = RequireComponent(handle, componentType);
        PropertyInfo property = FindComponentProperty(component, field);
        var array = (Array)(property.GetValue(component) ?? throw new InvalidOperationException($"'{field}' is null."));
        return array.GetValue(index);
    }

    /// <summary>Writes one element of an array-typed <c>[ScriptProperty(Mutable = true)]</c> field, undoably.</summary>
    [ScriptFunction]
    public void SetComponentArrayField(ScriptEntityHandle handle, string componentType, string field, int index, object? value)
    {
        SceneComponent component = RequireComponent(handle, componentType);
        PropertyInfo property = FindComponentProperty(component, field);
        if (!ScriptReflection.IsMutable(property))
        {
            throw new InvalidOperationException($"'{field}' is not mutable from scripts.");
        }

        var array = (Array)(property.GetValue(component) ?? throw new InvalidOperationException($"'{field}' is null."));
        Type elementType = property.PropertyType.GetElementType()
            ?? throw new InvalidOperationException($"'{field}' is not an array.");
        object? converted = ScriptEntityHandle.ConvertForClr(value, elementType);
        object? before = array.GetValue(index);
        var command = new ScriptComponentFieldEditCommand(component, $"{field}[{index}]",
            () => array.SetValue(converted, index),
            () => array.SetValue(before, index));
        command.Apply();
        _context.EditSessions.Record(command);
    }

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

    private static SceneComponent RequireComponent(ScriptEntityHandle handle, string componentType) =>
        RequireSceneEntity(handle).Components.FirstOrDefault(component => component.TypeId == componentType)
            ?? throw new InvalidOperationException($"Entity has no '{componentType}' component.");

    private static PropertyInfo FindComponentProperty(SceneComponent component, string field) =>
        ScriptReflection.Properties(component.GetType()).FirstOrDefault(p => p.Name == field)
            ?? throw new InvalidOperationException($"'{field}' is not a script-visible property of {component.GetType().Name}.");

    // Delegates to the self-registering component registry — the same one the inspector's own "Add
    // Component" UI goes through (ISceneComponentType.Create()) — rather than hardcoding a switch over
    // a few built-in types, so any current or future plugin-registered component (like WoW lights) is
    // creatable from script without this module needing to know about it.
    private SceneComponent CreateComponent(string typeId) =>
        _context.ComponentTypes.Find(typeId)?.Create()
            ?? throw new InvalidOperationException($"No scene component type named '{typeId}'.");

    private string DefaultName(string typeId) =>
        _context.ComponentTypes.Find(typeId)?.DisplayName ?? "Entity";
}
