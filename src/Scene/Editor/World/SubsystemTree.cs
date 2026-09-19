using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Everything alive in the editor, found the way <see cref="WorldLifecycle"/> and
/// <see cref="FrameLoop"/> both need it: the core spine members <see cref="EditorContext"/>
/// constructs directly (not <see cref="ISubsystem"/>s, so nothing else would ever find them), then a
/// recursive descent through <see cref="ISubsystemHost.Subsystems"/> — <c>EditorContext → MenuBarManager
/// → WindowManager → Window</c>, <c>DatabaseSystem → Storage</c>, and so on — which reaches every
/// window and plugin without naming any of them.
/// </summary>
public static class SubsystemTree
{
    /// <summary>Every node once, deduplicated by reference: spine first, then the tree.</summary>
    public static IReadOnlyList<object> Walk(EditorContext context)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = new List<object>();

        void Visit(object? node)
        {
            if (node is null || !seen.Add(node))
            {
                return;
            }

            result.Add(node);

            if (node is ISubsystemHost host)
            {
                foreach (ISubsystem subsystem in host.Subsystems)
                {
                    Visit(subsystem);
                }
            }
        }

        Visit(context.Selection);
        Visit(context.Clipboard);
        Visit(context.EditSessions);
        Visit(context.Scene);
        Visit(context.Catalog);
        Visit(context.Problems);
        Visit(context.Tools);
        Visit(context.Database);
        Visit(context.Maps);
        Visit(context.ModelFormats);
        Visit(context.Assets);
        Visit(context.MeshMaterials);
        Visit(context.Landscape);
        Visit(context.Procedural);
        Visit(context.Images);
        Visit(context.Prefabs);
        Visit(context.Streaming);
        Visit(context.Environments);
        Visit(context.Batch);
        Visit(context.ComponentTypes);
        Visit(context.MapProperties);
        Visit(context.Scripting);
        Visit(context.Migrations);
        Visit(context.Clock);
        Visit(context.Shortcuts);
        Visit(context.ReferenceLabels);

        // Recurses into every self-registered subsystem, and any plugin type that hangs directly off
        // EditorContext the same way.
        Visit(context);

        return result;
    }
}
