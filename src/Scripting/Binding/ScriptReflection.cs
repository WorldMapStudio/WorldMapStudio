using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WorldMapStudio;

/// <summary>
/// Finds [ScriptProperty]/[ScriptFunction] members via reflection. Shared by the JS engine's member
/// filter, <see cref="ScriptEntityHandle"/>'s generic Get/Set, and the .d.ts generator so none of them
/// can disagree about what's exposed — see ScriptingDesign.md's "bindings and declarations are the
/// same reflection pass" principle.
/// </summary>
public static class ScriptReflection
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>Whether a reflected member should be visible to scripts.</summary>
    public static bool IsVisible(MemberInfo member) => member switch
    {
        PropertyInfo property => GetAttribute<ScriptPropertyAttribute>(property, property.GetMethod) is not null,
        MethodInfo method => GetAttribute<ScriptFunctionAttribute>(method, method) is not null,
        _ => false,
    };

    /// <summary>Every [ScriptProperty] on a type, including ones only attributed on a base declaration.</summary>
    public static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(PublicInstance).Where(p => GetAttribute<ScriptPropertyAttribute>(p, p.GetMethod) is not null);

    /// <summary>Every [ScriptFunction] on a type, including ones only attributed on a base declaration.</summary>
    public static IEnumerable<MethodInfo> Functions(Type type) =>
        type.GetMethods(PublicInstance).Where(m => !m.IsSpecialName && GetAttribute<ScriptFunctionAttribute>(m, m) is not null);

    /// <summary>Whether a [ScriptProperty] was declared with <c>Mutable = true</c>.</summary>
    public static bool IsMutable(PropertyInfo property) =>
        GetAttribute<ScriptPropertyAttribute>(property, property.GetMethod)?.Mutable ?? false;

    /// <summary>
    /// Reads the attribute off <paramref name="member"/> directly, then — because a virtual override's
    /// <see cref="MemberInfo"/> does not inherit attributes from the base declaration it overrides
    /// (e.g. <c>EmptyEntity.DisplayName</c> overriding <c>Entity.DisplayName</c>, which is where
    /// [ScriptProperty] actually lives) — walks up to the root virtual slot's declaring type and
    /// checks the same-named member there too.
    /// </summary>
    private static TAttribute? GetAttribute<TAttribute>(MemberInfo member, MethodInfo? virtualSlot)
        where TAttribute : Attribute
    {
        if (member.GetCustomAttribute<TAttribute>() is { } direct)
        {
            return direct;
        }

        if (virtualSlot is not { IsVirtual: true })
        {
            return null;
        }

        MethodInfo baseSlot = virtualSlot.GetBaseDefinition();
        if (ReferenceEquals(baseSlot, virtualSlot) || baseSlot.DeclaringType is not { } declaringType)
        {
            return null;
        }

        MemberInfo? baseMember = member switch
        {
            PropertyInfo => declaringType.GetProperty(member.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            MethodInfo => baseSlot,
            _ => null,
        };

        return baseMember?.GetCustomAttribute<TAttribute>();
    }
}
