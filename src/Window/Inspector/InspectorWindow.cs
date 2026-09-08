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
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.I, ShortcutModifiers.Alt);

    private readonly SelectionSystem _selection;
    private readonly EditSessionManager _sessions;
    private string _fieldFilter = string.Empty;

    public InspectorWindow(WindowManager manager)
        : base("Inspector", defaultSize: new Vector2(300, 400))
    {
        Context = manager.Context;
        _selection = manager.Context.Selection;
        _sessions = manager.Context.EditSessions;
        InitializeSubsystems();
    }

    /// <summary>The editor context, so a registered inspector can reach shared systems.</summary>
    public EditorContext Context { get; }

    private IEnumerable<IEntityInspector> Inspectors => Subsystems.Cast<IEntityInspector>();

    protected override void DrawContent()
    {
        // Must run every frame regardless of selection: a component can open a modal from inside
        // DrawInspector, and that modal needs to keep drawing even if the selection changes underneath it.
        foreach (ISceneComponentType type in Context.ComponentTypes.All)
        {
            type.DrawModals();
        }

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

        string filter = string.Empty;
        if (inspector.ShowFieldFilter)
        {
            ImGuiEx.FieldFilterInput("##fieldfilter", ref _fieldFilter);
            ImGui.Spacing();
            filter = _fieldFilter;
        }

        var context = new InspectorContext(_sessions, new FieldFilter(filter));
        inspector.Draw(context, selected);
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
