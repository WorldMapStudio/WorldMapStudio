using System;

namespace WorldMapStudio;

/// <summary>Exposes a method to JS as a callable function.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ScriptFunctionAttribute : Attribute
{
}
