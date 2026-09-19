using System;

namespace WorldMapStudio;

/// <summary>
/// Declares the type every subsystem of this host must be. The generator rejects a subsystem that
/// does not satisfy it and emits a <c>Subsystems</c> property typed to it, so the host never casts.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SubsystemHostAttribute(Type contract) : Attribute
{
    public Type Contract { get; } = contract;
}
