using System.Threading;

namespace WorldMapStudio;

/// <summary>
/// Runtime identity for an entity, unique within a session. This is not a database key: how an
/// entity persists is the entity's own business and may span tables we don't control.
/// </summary>
public readonly record struct EntityId([property: ScriptProperty] long Value)
{
    private static long _next;

    public static EntityId Next() => new(Interlocked.Increment(ref _next));

    public override string ToString() => $"#{Value}";
}
