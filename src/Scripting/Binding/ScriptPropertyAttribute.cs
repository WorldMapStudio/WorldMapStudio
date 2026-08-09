using System;

namespace WorldMapStudio;

/// <summary>
/// Exposes a property to JS. Read-only for now: <see cref="Mutable"/> is forward-declared surface
/// for Phase 9 (see ScriptingPlan.md), where a write is routed through a generic edit command instead
/// of the raw CLR setter; the binder does not yet honour it, so a settable property marked here is
/// still read-only from JS today.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ScriptPropertyAttribute : Attribute
{
    public bool Mutable { get; init; }
}
