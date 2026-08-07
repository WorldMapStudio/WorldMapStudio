using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Base for a type-safe inspector over entities of type <typeparamref name="T"/>.</summary>
public abstract class EntityInspector<T> : IEntityInspector where T : class, IEntity
{
    public Type TargetType => typeof(T);

    public virtual float Priority => 0f;

    public void Draw(InspectorContext context, IReadOnlyList<IEntity> targets)
    {
        var typed = new List<T>(targets.Count);
        foreach (IEntity entity in targets)
        {
            typed.Add((T)entity);
        }

        DrawTargets(context, typed);
    }

    protected abstract void DrawTargets(InspectorContext context, IReadOnlyList<T> targets);
}
