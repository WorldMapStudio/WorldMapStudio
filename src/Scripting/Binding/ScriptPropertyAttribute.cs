using System;

namespace WorldMapStudio;

/// <summary>
/// Exposes a property to JS. Read-only unless <see cref="Mutable"/> is set, in which case
/// <see cref="ScriptEntityHandle.Set"/> allows the write and records it as an undoable
/// <see cref="ScriptPropertyEditCommand"/>, the same edit-session bookkeeping every other UI path goes
/// through.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ScriptPropertyAttribute : Attribute
{
    public bool Mutable { get; init; }
}
