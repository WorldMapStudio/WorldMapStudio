using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Discovers <see cref="StyleTokensAttribute"/> classes by reflection and forces their static
/// constructor to run (same trick every other self-registering system in the editor style uses, here
/// applied to field initializers instead of a scanned interface). Each <see cref="StyleColor"/>/
/// <see cref="StyleFont"/>/<see cref="StyleSize"/> registers itself as its declaring class
/// initializes, so this never names a token class directly.
/// </summary>
public static class StyleTokenRegistry
{
    private static readonly List<StyleColor> ColorList = [];
    private static readonly List<StyleFont> FontList = [];
    private static readonly List<StyleSize> SizeList = [];
    private static readonly HashSet<string> ColorIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FontIds = new(StringComparer.Ordinal);
    private static readonly HashSet<string> SizeIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StyleColorValue> Defaults = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, float> SizeDefaultsMap = new(StringComparer.Ordinal);

    private static bool _discovered;

    public static IReadOnlyList<StyleColor> Colors => ColorList;
    public static IReadOnlyList<StyleFont> Fonts => FontList;
    public static IReadOnlyList<StyleSize> Sizes => SizeList;

    public static IReadOnlyDictionary<string, StyleColorValue> ColorDefaults => Defaults;
    public static IReadOnlyDictionary<string, float> SizeDefaults => SizeDefaultsMap;

    /// <summary>Scans the assembly for <see cref="StyleTokensAttribute"/> classes and runs their
    /// static constructor. Idempotent; call from <see cref="EditorStyle.Initialize"/> before the
    /// first resolve.</summary>
    public static void EnsureDiscovered()
    {
        if (_discovered)
        {
            return;
        }

        _discovered = true;

        foreach (Type type in SafeGetTypes(typeof(StyleTokenRegistry).Assembly))
        {
            if (type.GetCustomAttribute<StyleTokensAttribute>() is null)
            {
                continue;
            }

            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception e)
            {
                GD.PushError($"[Style] Failed to initialize token class '{type.FullName}': {e.Message}");
            }
        }
    }

    internal static void RegisterColor(StyleColor color)
    {
        if (!ColorIds.Add(color.Id))
        {
            GD.PushWarning($"[Style] Duplicate color token id '{color.Id}' ignored.");
            return;
        }

        ColorList.Add(color);
        Defaults[color.Id] = color.Default;
    }

    internal static void RegisterFont(StyleFont font)
    {
        if (!FontIds.Add(font.Id))
        {
            GD.PushWarning($"[Style] Duplicate font slot id '{font.Id}' ignored.");
            return;
        }

        FontList.Add(font);
    }

    internal static void RegisterSize(StyleSize size)
    {
        if (!SizeIds.Add(size.Id))
        {
            GD.PushWarning($"[Style] Duplicate size token id '{size.Id}' ignored.");
            return;
        }

        SizeList.Add(size);
        SizeDefaultsMap[size.Id] = size.DefaultValue;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(static t => t is not null)!;
        }
    }
}
