using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Entity tags, exposed to JS as <c>wms.tags</c>: the tag catalog, tagging entities, and which entities
/// the Object tool may target. Everywhere a tag is asked for, its name or its id works.
/// </summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class TagsScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "tags";

    public TagsScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    private TagSystem Tags => _context.Tags;

    /// <summary>Every tag, by name, with how many loaded entities carry it.</summary>
    [ScriptFunction]
    public TagDescriptor[] List() =>
        Tags.Definitions.Select(tag => new TagDescriptor(tag, Tags.LoadedCount(tag.RecordId ?? -1))).ToArray();

    /// <summary>Creates a tag, undoably, and returns its id. <paramref name="color"/> is packed 0xRRGGBB;
    /// omitted, one is picked.</summary>
    [ScriptFunction]
    public int Create(string name, double? color = null) =>
        Tags.Create(name, color is { } value ? (int)value : null).RecordId!.Value;

    /// <summary>Renames a tag, undoably.</summary>
    [ScriptFunction]
    public void Rename(object tag, string name) => Tags.Rename(Require(tag), name);

    /// <summary>Recolours a tag, undoably. <paramref name="color"/> is packed 0xRRGGBB.</summary>
    [ScriptFunction]
    public void SetColor(object tag, double color) => Tags.SetColor(Require(tag), (int)color);

    /// <summary>Deletes a tag and takes it off every loaded entity, as one undo step.</summary>
    [ScriptFunction]
    public void Delete(object tag) => Tags.Delete(Require(tag));

    /// <summary>The ids of the tags an entity carries.</summary>
    [ScriptFunction]
    public int[] Get(ScriptEntityHandle entity) => RequireScene(entity).Tags.ToArray();

    /// <summary>Gives every entity the tag, undoably. Entities that can't be tagged are skipped; returns
    /// how many gained it.</summary>
    [ScriptFunction]
    public int Add(ScriptEntityHandle[] entities, object tag) => Tags.Add(RequireScene(entities), Require(tag).RecordId!.Value);

    /// <summary>Takes the tag off every entity that has it, undoably. Returns how many lost it.</summary>
    [ScriptFunction]
    public int Remove(ScriptEntityHandle[] entities, object tag) => Tags.Remove(RequireScene(entities), Require(tag).RecordId!.Value);

    /// <summary>Replaces every entity's tags with exactly this set, undoably. Returns how many changed.</summary>
    [ScriptFunction]
    public int Set(ScriptEntityHandle[] entities, object[] tags) => Tags.Set(RequireScene(entities), ResolveIds(tags));

    /// <summary>The loaded entities in view that carry the tag.</summary>
    [ScriptFunction]
    public ScriptEntityHandle[] Find(object tag)
    {
        int id = Require(tag).RecordId!.Value;
        return _context.Scene.InView
            .Where(entity => entity.Tags.Contains(id))
            .Select(entity => new ScriptEntityHandle(_context.Scene, _context.Catalog, _context.EditSessions, entity))
            .ToArray();
    }

    /// <summary>Which entities the Object tool may target: only ones carrying an included tag (plus
    /// untagged ones, when asked), never ones carrying an excluded tag.</summary>
    [ScriptFunction]
    public ObjectFilterDescriptor GetObjectFilter() => new(Tags.ObjectFilter);

    /// <summary>Sets which entities the Object tool may target, and drops anything selected that no
    /// longer qualifies. Empty lists lift the constraint.</summary>
    [ScriptFunction]
    public void SetObjectFilter(object[] include, object[] exclude, bool includeUntagged = false)
    {
        EntityTagSet included = EntityTagSet.From(ResolveIds(include));
        EntityTagSet excluded = EntityTagSet.From(ResolveIds(exclude));
        if (included.Overlaps(excluded))
        {
            throw new InvalidOperationException("A tag can't be both included and excluded.");
        }

        Tags.SetObjectFilter(included, excluded, includeUntagged && !included.IsEmpty);
    }

    // A tag by name or by id. A name that matches nothing, or an id that isn't a tag, is an error rather
    // than a silent no-op, so a misspelt call doesn't quietly tag nothing.
    private EntityTagDefinition Require(object tag)
    {
        if (tag is string name)
        {
            return Tags.FindByName(name) ?? throw new InvalidOperationException($"No tag named '{name}'.");
        }

        int id = Convert.ToInt32(tag, CultureInfo.InvariantCulture);
        return Tags.Find(id) ?? throw new InvalidOperationException($"No tag with id {id}.");
    }

    private IEnumerable<int> ResolveIds(IEnumerable<object> tags) => tags.Select(tag => Require(tag).RecordId!.Value).ToArray();

    private static SceneEntity RequireScene(ScriptEntityHandle handle) =>
        handle.Resolve() as SceneEntity ?? throw new InvalidOperationException("That handle does not refer to a scene entity.");

    private static SceneEntity[] RequireScene(IEnumerable<ScriptEntityHandle> handles) => handles.Select(RequireScene).ToArray();
}

/// <summary>A tag as <c>wms.tags</c> reports it.</summary>
public sealed class TagDescriptor
{
    public TagDescriptor(EntityTagDefinition tag, int loadedCount)
    {
        Id = tag.RecordId ?? -1;
        Name = tag.Name;
        Color = tag.Color;
        LoadedCount = loadedCount;
    }

    [ScriptProperty] public int Id { get; }
    [ScriptProperty] public string Name { get; }
    [ScriptProperty] public int Color { get; }
    [ScriptProperty] public int LoadedCount { get; }
}

/// <summary>The Object tool's target filter as <c>wms.tags</c> reports it.</summary>
public sealed class ObjectFilterDescriptor
{
    public ObjectFilterDescriptor(ObjectTargetFilter filter)
    {
        Include = filter.Include.ToArray();
        Exclude = filter.Exclude.ToArray();
        IncludeUntagged = filter.IncludeUntagged;
    }

    [ScriptProperty] public int[] Include { get; }
    [ScriptProperty] public int[] Exclude { get; }
    [ScriptProperty] public bool IncludeUntagged { get; }
}
