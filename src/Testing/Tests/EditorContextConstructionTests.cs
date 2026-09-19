using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers the order <see cref="EditorContext"/> builds its core systems in. Subsystems are built after
/// every one of them, but a core system built early can still capture a sibling that does not exist
/// yet, and that shows up as a null field of a core system's type.
/// </summary>
public static class EditorContextConstructionTests
{
    [EditorTest(Category = "EditorContext", Thread = TestThread.Background)]
    public static void No_system_captures_a_core_system_before_it_is_built()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_construction_order_test__" });
        HashSet<Type> spineTypes = context.Spine.Select(member => member.GetType()).ToHashSet();

        var captured = new List<string>();
        foreach (object node in SubsystemTree.Walk(context))
        {
            for (Type? type = node.GetType(); type is not null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (FieldInfo field in type.GetFields(flags).Where(field => spineTypes.Contains(field.FieldType)))
                {
                    if (field.GetValue(node) is null)
                    {
                        captured.Add($"{node.GetType().Name}.{field.Name} ({field.FieldType.Name})");
                    }
                }
            }
        }

        Assert.IsTrue(captured.Count == 0, $"captured before it was built: {string.Join(", ", captured)}");
    }
}
