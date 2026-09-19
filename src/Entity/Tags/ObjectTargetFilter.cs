namespace WorldMapStudio;

/// <summary>
/// Which scene entities the Object tool may target — click, marquee and gizmo. Owned by
/// <see cref="TagSystem"/>, outside the tool, so scripts can set it whether or not the tool is active.
/// Only decides targeting: it doesn't hide anything.
/// </summary>
public sealed class ObjectTargetFilter
{
    /// <summary>Only entities carrying at least one of these are targetable. Empty means no such constraint.</summary>
    public EntityTagSet Include { get; private set; }

    /// <summary>Entities carrying any of these are never targetable.</summary>
    public EntityTagSet Exclude { get; private set; }

    /// <summary>Whether entities with no tags at all pass an <see cref="Include"/> constraint. Meaningless
    /// while <see cref="Include"/> is empty.</summary>
    public bool IncludeUntagged { get; private set; }

    /// <summary>Bumped on every change, so a cache built over the filter knows when to rebuild.</summary>
    public int Version { get; private set; }

    /// <summary>Whether any tag constraint is set. While one is, derived content (terrain chunks) can't
    /// carry tags and so isn't targetable — otherwise it would answer every click that missed a tagged entity.</summary>
    public bool IsActive => !Include.IsEmpty || !Exclude.IsEmpty;

    public bool Targets(SceneEntity entity)
    {
        if (entity is IDerivedEntity)
        {
            return !IsActive;
        }

        return !entity.Tags.Overlaps(Exclude)
            && (Include.IsEmpty || entity.Tags.Overlaps(Include) || (IncludeUntagged && entity.Tags.IsEmpty));
    }

    /// <summary>Replaces the filter. Returns whether anything changed.</summary>
    public bool Set(EntityTagSet include, EntityTagSet exclude, bool includeUntagged)
    {
        if (include == Include && exclude == Exclude && includeUntagged == IncludeUntagged)
        {
            return false;
        }

        Include = include;
        Exclude = exclude;
        IncludeUntagged = includeUntagged;
        Version++;
        return true;
    }
}
