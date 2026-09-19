#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Discovers <see cref="EditorTestAttribute"/>-marked methods by reflection and adapts their various
/// allowed signatures into uniform <see cref="TestCase"/> invokers.
///
/// Supported signatures (static or instance, instance needs a public parameterless ctor):
/// <c>void M()</c>, <c>void M(TestContext)</c>, <c>Task M()</c>, <c>Task M(TestContext)</c>.
///
/// This deliberately scans instead of using <c>[Subsystem]</c>: it finds methods by attribute across
/// several signatures rather than types with one constructor shape, and tests are not nodes in the
/// running editor's subsystem tree.
/// </summary>
public static class TestRegistry
{
    /// <summary>Scans the given assemblies (or the calling assembly) for tests, ordered by category then name.</summary>
    public static IReadOnlyList<TestCase> Discover(params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetExecutingAssembly() };
        }

        List<TestCase> cases = new();
        HashSet<string> seenIds = new();

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in AssemblyTypes.SafeGetTypes(assembly))
            {
                MethodInfo[] methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (MethodInfo method in methods)
                {
                    EditorTestAttribute? attr = method.GetCustomAttribute<EditorTestAttribute>();
                    if (attr is null)
                    {
                        continue;
                    }

                    if (!TryBuildCase(type, method, attr, out TestCase? testCase, out string? error))
                    {
                        GD.PushWarning($"[Tests] Skipping {type.Name}.{method.Name}: {error}");
                        continue;
                    }

                    if (!seenIds.Add(testCase!.Id))
                    {
                        GD.PushWarning($"[Tests] Duplicate test id '{testCase.Id}' ignored.");
                        continue;
                    }

                    cases.Add(testCase);
                }
            }
        }

        return cases
            .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryBuildCase(Type type, MethodInfo method, EditorTestAttribute attr, out TestCase? testCase, out string? error)
    {
        testCase = null;
        error = null;

        ParameterInfo[] parameters = method.GetParameters();
        bool takesContext = parameters.Length == 1 && parameters[0].ParameterType == typeof(TestContext);
        if (parameters.Length != 0 && !takesContext)
        {
            error = "unsupported parameters (expected none or a single TestContext).";
            return false;
        }

        bool returnsTask = typeof(Task).IsAssignableFrom(method.ReturnType);
        if (!returnsTask && method.ReturnType != typeof(void))
        {
            error = "unsupported return type (expected void or Task).";
            return false;
        }

        object? instance = null;
        if (!method.IsStatic)
        {
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                error = $"could not create instance ({e.GetType().Name}); add a public parameterless constructor.";
                return false;
            }
        }

        string category = string.IsNullOrWhiteSpace(attr.Category) ? type.Name : attr.Category!;
        string name = string.IsNullOrWhiteSpace(attr.Name) ? Prettify(method.Name) : attr.Name!;
        string id = $"{category}.{name}";

        Func<TestContext, Task> invoke = async ctx =>
        {
            object?[] args = takesContext ? new object?[] { ctx } : Array.Empty<object?>();
            object? result;
            try
            {
                result = method.Invoke(instance, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                // Unwrap so the real assertion/exception (not the reflection wrapper) is reported.
                throw tie.InnerException;
            }

            if (result is Task task)
            {
                await task;
            }
        };

        testCase = new TestCase(id, name, category, attr.Thread, attr.Skip, invoke);
        return true;
    }

    private static string Prettify(string methodName) => methodName.Replace('_', ' ');
}
