using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// The landscape functions available to materials, discovered by reflection over the loaded
/// assemblies at startup.
///
/// This is the one place the editor deliberately departs from compile-time <c>[Subsystem]</c>
/// registration: functions are meant to be able to live in an assembly loaded (and reloaded) at
/// runtime, which a source generator cannot see. Discovery is therefore tolerant — a type that fails
/// to construct is reported and skipped rather than taking the editor down with it.
/// </summary>
public sealed class LandscapeFunctions
{
    private readonly Dictionary<string, ILandscapeFunction> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];

    /// <summary>Every discovered function, ordered by display name.</summary>
    public IReadOnlyList<ILandscapeFunction> All { get; private set; } = [];

    public IEnumerable<ILandscapeAlphaFunction> Alpha => All.OfType<ILandscapeAlphaFunction>();

    public IEnumerable<ILandscapeHeightFunction> Height => All.OfType<ILandscapeHeightFunction>();

    public IEnumerable<ILandscapeHoleFunction> Hole => All.OfType<ILandscapeHoleFunction>();

    /// <summary>Types that looked like functions but could not be used, with the reason.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Bumps on every rescan, so views and cached bindings can tell when to refresh.</summary>
    public int Version { get; private set; }

    /// <summary>The function with this id, or null if nothing (or nothing loaded) provides it.</summary>
    public ILandscapeFunction? Find(string id) =>
        id.Length > 0 && _byId.TryGetValue(id, out ILandscapeFunction? function) ? function : null;

    public ILandscapeAlphaFunction? FindAlpha(string id) => Find(id) as ILandscapeAlphaFunction;

    public ILandscapeHeightFunction? FindHeight(string id) => Find(id) as ILandscapeHeightFunction;

    public ILandscapeHoleFunction? FindHole(string id) => Find(id) as ILandscapeHoleFunction;

    /// <summary>
    /// (Re)scans for functions. Safe to call again after loading an assembly; existing materials keep
    /// their bindings because they store ids, not instances.
    /// </summary>
    public void Discover(params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
        {
            assemblies = CandidateAssemblies();
        }

        // Public top-level types only. Beyond being a reasonable thing to ask of a function, this is
        // what keeps helper implementations — test doubles, nested experiments — out of the list a
        // user picks from.
        List<Type> candidates = assemblies
            .SelectMany(SafeGetTypes)
            .Where(type => type.IsPublic && IsFunctionType(type))
            .ToList();

        DiscoverFrom(candidates);

        foreach (string warning in _warnings)
        {
            GD.PushWarning($"[Landscape] Function skipped — {warning}");
        }
    }

    /// <summary>
    /// Registers an explicit set of types, skipping the assembly scan. Used by tests, and by anything
    /// that already knows exactly which functions it is providing.
    /// </summary>
    public void DiscoverFrom(IEnumerable<Type> types)
    {
        _byId.Clear();
        _warnings.Clear();

        foreach (Type type in types)
        {
            if (!IsFunctionType(type))
            {
                continue;
            }

            if (TryCreate(type, out ILandscapeFunction? function, out string? error))
            {
                Register(function!, type);
            }
            else
            {
                _warnings.Add($"{type.Name}: {error}");
            }
        }

        All = _byId.Values.OrderBy(function => function.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Version++;
    }

    private void Register(ILandscapeFunction function, Type type)
    {
        if (function.Id.Trim().Length == 0)
        {
            _warnings.Add($"{type.Name}: has an empty Id.");
            return;
        }

        // Ids are what materials store, so a collision would silently rebind materials to whichever
        // type happened to be scanned first.
        if (_byId.TryGetValue(function.Id, out ILandscapeFunction? existing))
        {
            _warnings.Add($"{type.Name}: id '{function.Id}' is already used by {existing.GetType().Name}.");
            return;
        }

        if (function.MaxSampleRadius < 0.0f)
        {
            _warnings.Add($"{type.Name}: negative sample radius.");
            return;
        }

        if (DuplicateParameter(function) is { } duplicate)
        {
            _warnings.Add($"{type.Name}: declares parameter '{duplicate}' twice.");
            return;
        }

        _byId[function.Id] = function;
    }

    private static string? DuplicateParameter(ILandscapeFunction function)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (LandscapeParameter parameter in function.Parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                return parameter.Name;
            }
        }

        return null;
    }

    private static bool IsFunctionType(Type type) =>
        typeof(ILandscapeFunction).IsAssignableFrom(type) &&
        type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false };

    private static bool TryCreate(Type type, out ILandscapeFunction? function, out string? error)
    {
        function = null;
        error = null;

        if (type.GetConstructor(Type.EmptyTypes) == null)
        {
            error = "needs a public parameterless constructor.";
            return false;
        }

        try
        {
            function = (ILandscapeFunction)Activator.CreateInstance(type)!;
            return true;
        }
        catch (Exception e)
        {
            error = $"constructor threw {e.GetType().Name}: {e.Message}";
            return false;
        }
    }

    // Only this assembly and anything referencing it. A function has to implement ILandscapeFunction,
    // so it cannot exist in an assembly that does not reference ours — and walking EF Core and the
    // BCL for types that cannot qualify would cost real startup time for nothing.
    private static Assembly[] CandidateAssemblies()
    {
        Assembly self = typeof(ILandscapeFunction).Assembly;
        string selfName = self.GetName().Name ?? "";

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly == self || References(assembly, selfName))
            .ToArray();
    }

    private static bool References(Assembly assembly, string name)
    {
        try
        {
            return assembly.GetReferencedAssemblies().Any(reference => reference.Name == name);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            // A partially loadable assembly still yields the types that did load.
            return e.Types.Where(type => type != null)!;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
