using System;

namespace WorldMapStudio;

/// <summary>
/// Marks a static class as declaring style tokens (<see cref="StyleColor"/>/<see cref="StyleFont"/>/
/// <see cref="StyleSize"/> fields). <see cref="StyleTokenRegistry"/> discovers these by reflection and
/// forces their static constructor to run, so every token is registered up-front — the Style Editor
/// window lists them without anything referencing a token class by name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StyleTokensAttribute : Attribute
{
}
