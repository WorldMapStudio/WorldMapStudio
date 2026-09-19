using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Everything the editor does with tags: the tag catalog (create, rename, recolor, delete) and putting
/// tags on entities, every step of it undoable through the edit session. A plain member of
/// <see cref="EditorContext"/> so the inspector, the tags window and scripting share one implementation
/// whether or not any tool is active.
/// </summary>
public sealed class TagSystem(EditorContext context)
{
    // Cycled through for a tag created without a colour, so neighbouring tags are told apart at a glance.
    private static readonly int[] Palette =
    [
        0xE05A47, 0x4FA3E0, 0x5CB85C, 0xE0B341, 0xA06CD5, 0x3CC6C6, 0xE07AB0, 0x8C8C8C,
    ];

    /// <summary>Which entities the Object tool may target.</summary>
    public ObjectTargetFilter ObjectFilter { get; } = new();

    public IEnumerable<EntityTagDefinition> Definitions =>
        context.Catalog.OfType<EntityTagDefinition>().OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

    public EntityTagDefinition? Find(int id) =>
        context.Catalog.OfType<EntityTagDefinition>().FirstOrDefault(tag => tag.RecordId == id);

    public EntityTagDefinition? FindByName(string name) =>
        context.Catalog.OfType<EntityTagDefinition>().FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="entity"/> can carry tags: it must have somewhere to keep them. An
    /// editor-authored entity always does; an entity stored elsewhere does only when its factory bridges
    /// it into the entity table. Derived content is recomputed and has nowhere.
    /// </summary>
    public bool CanTag(SceneEntity entity) =>
        entity is not IDerivedEntity
        && (entity is MapSceneEntity || context.Database.SceneSources.FactoryFor(entity)?.BridgeSource != null);

    /// <summary>Null when <paramref name="name"/> is usable as a new tag's name, otherwise why not.</summary>
    public string? NameProblem(string name, EntityTagDefinition? renaming = null)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "A tag needs a name.";
        }

        if (trimmed.Length > EntityTagDefinitionFactory.NameMaxLength)
        {
            return $"Tag names are at most {EntityTagDefinitionFactory.NameMaxLength} characters.";
        }

        if (trimmed.Any(character => char.IsWhiteSpace(character) || character == ':'))
        {
            return "Tag names can't contain spaces or ':', so they stay searchable as tag:<name>.";
        }

        return FindByName(trimmed) is { } existing && !ReferenceEquals(existing, renaming)
            ? $"A tag named '{trimmed}' already exists."
            : null;
    }

    /// <summary>
    /// Creates a tag, undoably, and — when <paramref name="tagWith"/> is given — puts it on those entities
    /// in the same undo step. Throws with the reason when the name isn't usable.
    /// </summary>
    public EntityTagDefinition Create(string name, int? color = null, IEnumerable<SceneEntity>? tagWith = null)
    {
        if (NameProblem(name) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var tag = new EntityTagDefinition { Name = name.Trim() };
        context.Catalog.AssignId(tag);
        tag.Color = color ?? Palette[(tag.RecordId!.Value - 1) % Palette.Length];

        var commands = new List<IEditCommand> { new CreateCatalogEntityCommand(context.Catalog, tag) };
        SceneEntity[] targets = tagWith?.Where(CanTag).Distinct().ToArray() ?? [];
        if (targets.Length > 0)
        {
            commands.Add(new SetEntityTagsCommand(
                context.Scene,
                targets,
                targets.Select(entity => entity.Tags).ToArray(),
                targets.Select(entity => entity.Tags.With(tag.RecordId!.Value)).ToArray()));
        }

        var command = new BatchEditCommand($"Create tag '{tag.Name}'", commands);
        command.Apply();
        context.EditSessions.Record(command);
        return tag;
    }

    public void Rename(EntityTagDefinition tag, string name)
    {
        if (NameProblem(name, tag) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        string trimmed = name.Trim();
        if (trimmed == tag.Name)
        {
            return;
        }

        var command = new SetFieldCommand<string>(tag, "name", value => tag.Name = value, tag.Name, trimmed);
        command.Apply();
        context.EditSessions.Record(command);
    }

    public void SetColor(EntityTagDefinition tag, int color)
    {
        color &= 0xFFFFFF;
        if (color == tag.Color)
        {
            return;
        }

        var command = new SetFieldCommand<int>(tag, "colour", value => tag.Color = value, tag.Color, color);
        command.Apply();
        context.EditSessions.Record(command);
    }

    /// <summary>
    /// Deletes a tag and takes it off every entity that is loaded, as one undo step. Entities that aren't
    /// loaded lose it when the commit's foreign-key cascade removes the tag's rows.
    /// </summary>
    public void Delete(EntityTagDefinition tag)
    {
        var commands = new List<IEditCommand>();
        if (tag.RecordId is int id)
        {
            SceneEntity[] carriers = context.Scene.Entities.Where(entity => entity.Tags.Contains(id)).ToArray();
            if (carriers.Length > 0)
            {
                commands.Add(new SetEntityTagsCommand(
                    context.Scene,
                    carriers,
                    carriers.Select(entity => entity.Tags).ToArray(),
                    carriers.Select(entity => entity.Tags.Without(id)).ToArray()));
            }
        }

        commands.Add(new DeleteCatalogEntityCommand(context.Catalog, tag));

        var batch = new BatchEditCommand($"Delete tag '{tag.Name}'", commands);
        batch.Apply();
        context.EditSessions.Record(batch);
    }

    /// <summary>Gives every taggable entity the tag, undoably. Returns how many gained it.</summary>
    public int Add(IEnumerable<SceneEntity> entities, int tagId) => Change(entities, tags => tags.With(tagId));

    /// <summary>Takes the tag off every entity that has it, undoably. Returns how many lost it.</summary>
    public int Remove(IEnumerable<SceneEntity> entities, int tagId) => Change(entities, tags => tags.Without(tagId));

    /// <summary>Replaces every taggable entity's tags with exactly <paramref name="tagIds"/>, undoably.</summary>
    public int Set(IEnumerable<SceneEntity> entities, IEnumerable<int> tagIds)
    {
        EntityTagSet target = EntityTagSet.From(tagIds);
        return Change(entities, _ => target);
    }

    /// <summary>
    /// Sets which entities the Object tool may target. Whatever is selected and no longer targetable
    /// leaves the selection, so the inspector and gizmo never go on editing something the tool has
    /// been told to leave alone.
    /// </summary>
    public void SetObjectFilter(EntityTagSet include, EntityTagSet exclude, bool includeUntagged)
    {
        if (!ObjectFilter.Set(include, exclude, includeUntagged))
        {
            return;
        }

        foreach (IEntity entity in context.Selection.Selected.ToArray())
        {
            if (entity is SceneEntity scene && !ObjectFilter.Targets(scene))
            {
                context.Selection.Remove(entity);
            }
        }
    }

    /// <summary>How many loaded entities carry the tag.</summary>
    public int LoadedCount(int tagId) => context.Scene.Entities.Count(entity => entity.Tags.Contains(tagId));

    private int Change(IEnumerable<SceneEntity> entities, Func<EntityTagSet, EntityTagSet> change)
    {
        var changed = new List<SceneEntity>();
        var before = new List<EntityTagSet>();
        var after = new List<EntityTagSet>();
        foreach (SceneEntity entity in entities.Distinct())
        {
            EntityTagSet next = change(entity.Tags);
            if (next != entity.Tags && CanTag(entity))
            {
                changed.Add(entity);
                before.Add(entity.Tags);
                after.Add(next);
            }
        }

        if (changed.Count == 0)
        {
            return 0;
        }

        var command = new SetEntityTagsCommand(context.Scene, changed.ToArray(), before.ToArray(), after.ToArray());
        command.Apply();
        context.EditSessions.Record(command);
        return changed.Count;
    }
}
