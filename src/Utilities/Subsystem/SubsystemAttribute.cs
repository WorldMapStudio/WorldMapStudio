namespace WorldMapStudio;

using System;

/// <summary>
/// Marks a class implementing <see cref="ISubsystem"/> as a subsystem of <paramref name="parent"/>.
/// A source generator emits a public "InitializeSubsystems" field/method pair on the parent
/// (which must be partial) that constructs the subsystem via "new Subsystem(this)" and exposes
/// all subsystems, ordered by <see cref="ISubsystem.Priority"/>, via a "Subsystems" property.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SubsystemAttribute(string parent) : Attribute
{
    public string Parent { get; } = parent;
}
