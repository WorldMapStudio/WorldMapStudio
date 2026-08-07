#nullable enable
using System;

namespace WorldMapStudio;

/// <summary>Which executor a test body runs on.</summary>
public enum TestThread
{
    /// <summary>Runs on the main thread. Required for anything that touches Godot nodes, the
    /// renderer, or ImGui state. This is the default because most editor state is main-thread only.</summary>
    Main,

    /// <summary>Runs on a background worker thread. Use for pure/CPU work that must not touch the
    /// scene tree. A test may still hop threads mid-body via <see cref="TestContext"/>.</summary>
    Background,
}

/// <summary>
/// Marks a method as an in-editor test, discovered by reflection and run from the Test Runner window.
///
/// The method may be static or instance (instance types need a public parameterless constructor),
/// and may optionally take a <see cref="TestContext"/> and/or return a <see cref="System.Threading.Tasks.Task"/>:
/// <code>
/// [EditorTest]
/// public static void Vector_math_adds() => Assert.AreEqual(3, 1 + 2);
///
/// [EditorTest(Category = "Scene", Thread = TestThread.Main)]
/// public static void Root_has_children(TestContext t) => Assert.IsTrue(t.EditorRoot.GetChildCount() > 0);
///
/// [EditorTest]
/// public static async Task Loads_async(TestContext t)
/// {
///     await t.SwitchToBackground();
///     // ... heavy work off the main thread ...
/// }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EditorTestAttribute : Attribute
{
    /// <summary>Display name. Defaults to the method name (with underscores shown as spaces).</summary>
    public string? Name { get; set; }

    /// <summary>Group the test is shown under. Defaults to the declaring type's name.</summary>
    public string? Category { get; set; }

    /// <summary>Which thread the body starts on. Defaults to <see cref="TestThread.Main"/>.</summary>
    public TestThread Thread { get; set; } = TestThread.Main;

    /// <summary>When set, the test is discovered but skipped, and this reason is shown.</summary>
    public string? Skip { get; set; }
}
