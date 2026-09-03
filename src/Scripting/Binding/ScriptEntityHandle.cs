using System;
using System.Linq;
using System.Reflection;

namespace WorldMapStudio;

/// <summary>
/// What actually crosses the JS boundary in place of a raw <see cref="Entity"/> reference. Carries
/// only an <see cref="EntityId"/> and re-resolves the live entity from the scene/catalog registries on
/// every access, so a script holding a handle across a stream-out or deletion fails loudly and
/// catchably instead of reading or writing into state nothing else tracks anymore (see
/// ScriptingDesign.md principle 4).
///
/// A script never sees this class's own members directly — <see cref="ScriptEngineHost"/> installs a
/// <see cref="EntityProxyHandler"/> that intercepts every property access on a handle, so JS reads and
/// writes the entity's actual [ScriptProperty]s by name (<c>entity.Name = "Lantern"</c>), not through
/// <see cref="Get"/>/<see cref="Set"/> as JS-visible methods. <see cref="Get"/>/<see cref="Set"/> stay
/// `internal` — the shared implementation both the proxy and this class's own unit tests call. The
/// <c>[ScriptProperty]</c> tags below are for the .d.ts generator only (documenting the two fields
/// every handle guarantees); they're otherwise inert since a handle never reaches Jint's normal
/// reflection path.
/// </summary>
public sealed class ScriptEntityHandle
{
    private readonly SceneEntityRegistry _scene;
    private readonly CatalogEntityRegistry _catalog;
    private readonly EditSessionManager _sessions;
    private readonly EntityId _id;

    public ScriptEntityHandle(SceneEntityRegistry scene, CatalogEntityRegistry catalog, EditSessionManager sessions, Entity entity)
    {
        _scene = scene;
        _catalog = catalog;
        _sessions = sessions;
        _id = entity.Id;
    }

    [ScriptProperty]
    public long Id => _id.Value;

    [ScriptProperty]
    public string DisplayName => Resolve().DisplayName;

    /// <summary>Reads any [ScriptProperty] on the entity's actual runtime type by name.</summary>
    internal object? Get(string property)
    {
        Entity entity = Resolve();
        return FindProperty(entity, property).GetValue(entity);
    }

    /// <summary>
    /// Writes a [ScriptProperty(Mutable = true)] by name: applies the value live, then records a
    /// <see cref="ScriptPropertyEditCommand"/> into the active edit session, exactly like a gizmo drag
    /// records <see cref="TransformEntitiesCommand"/> — undo/redo and commit work the same way either
    /// path got there.
    /// </summary>
    internal void Set(string property, object? value)
    {
        Entity entity = Resolve();
        PropertyInfo info = FindProperty(entity, property);
        if (!ScriptReflection.IsMutable(info))
        {
            throw new InvalidOperationException($"'{property}' is not mutable from scripts.");
        }

        object? converted = ConvertForClr(value, info.PropertyType);
        object? before = info.GetValue(entity);
        info.SetValue(entity, converted);
        _sessions.Record(new ScriptPropertyEditCommand(entity, info, before, converted));
    }

    /// <summary>
    /// Applies a command an entity's own <c>[ScriptFunction]</c> built and records it into the active
    /// edit session — the "recorded already-applied" convention every UI path already follows (see
    /// <see cref="SetFieldCommand{T}"/>). Recording is what makes the edit undoable *and* what makes
    /// <see cref="EditSessionManager.Commit"/> persist it at all, so a mutating script function that
    /// skipped this would be silently dropped on commit.
    /// </summary>
    internal void ApplyAndRecord(IEditCommand command)
    {
        command.Apply();
        _sessions.Record(command);
    }

    /// <summary>Resolves the live entity this handle points to. Internal — the proxy handler and modules use it directly.</summary>
    internal Entity Resolve()
    {
        SceneEntity? scene = _scene.Entities.FirstOrDefault(e => e.Id == _id);
        if (scene is not null)
        {
            return scene;
        }

        CatalogEntity? catalog = _catalog.Entities.FirstOrDefault(e => e.Id == _id);
        if (catalog is not null)
        {
            return catalog;
        }

        throw new InvalidOperationException($"Entity {_id} is no longer loaded — it may have streamed out, been deleted, or been unloaded from its catalog.");
    }

    private static PropertyInfo FindProperty(Entity entity, string property) =>
        ScriptReflection.Properties(entity.GetType()).FirstOrDefault(p => p.Name == property)
        ?? throw new InvalidOperationException($"'{property}' is not a script-visible property of {entity.GetType().Name}.");

    // Deliberately minimal: covers the primitive/enum widening JS's single number type needs, not
    // arbitrary CLR types. A property like a Godot Vector3/Transform3D needs a real conversion layer
    // this doesn't attempt yet — see ScriptingPlan.md's Phase 8 deviations.
    internal static object? ConvertForClr(object? value, Type targetType)
    {
        if (value is null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        // Nullable<T> (e.g. WowLightComponent.ParamIds' int? elements): convert against the underlying
        // type — a boxed non-nullable value implicitly widens into the Nullable<T> slot it's written
        // into (PropertyInfo.SetValue/Array.SetValue both do this), so there's nothing further to do.
        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (target.IsEnum)
        {
            return Enum.ToObject(target, Convert.ToInt64(value));
        }

        return Convert.ChangeType(value, target);
    }
}
