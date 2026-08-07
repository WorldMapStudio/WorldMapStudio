using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Shows and edits the current selection. Entity types register an <see cref="IEntityInspector"/>;
/// the inspector whose target type is the closest common ancestor of everything selected draws all
/// of them at once, with edits applied to every target as one command.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed partial class InspectorWindow : Window, ISubsystemHost
{
    private readonly SelectionSystem _selection;
    private readonly InspectorContext _context;

    public InspectorWindow(WindowManager manager)
        : base("Inspector", defaultSize: new Vector2(300, 400))
    {
        _selection = manager.Context.Selection;
        _context = new InspectorContext(manager.Context.EditSessions);
        InitializeSubsystems();
    }

    private IEnumerable<IEntityInspector> Inspectors => Subsystems.Cast<IEntityInspector>();

    protected override void DrawContent()
    {
        IReadOnlyList<IEntity> selected = _selection.Selected;
        if (selected.Count == 0)
        {
            ImGui.TextDisabled("Nothing selected.");
            return;
        }

        IEntityInspector? inspector = Resolve(selected);
        if (inspector == null)
        {
            ImGui.TextDisabled("No inspector for the selection.");
            return;
        }

        inspector.Draw(_context, selected);
    }

    // The most-derived registered inspector whose target type every selected entity is an instance
    // of. The common ancestors of a selection form a single-inheritance chain, so "most derived"
    // is well-defined.
    private IEntityInspector? Resolve(IReadOnlyList<IEntity> selected)
    {
        IEntityInspector? best = null;
        foreach (IEntityInspector candidate in Inspectors)
        {
            bool handlesAll = true;
            foreach (IEntity entity in selected)
            {
                if (!candidate.TargetType.IsInstanceOfType(entity))
                {
                    handlesAll = false;
                    break;
                }
            }

            if (handlesAll && (best == null || best.TargetType.IsAssignableFrom(candidate.TargetType)))
            {
                best = candidate;
            }
        }

        return best;
    }
}
