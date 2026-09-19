using System.Collections.Generic;
using System.Linq;

namespace WorldMapStudio;

/// <summary>
/// Owns the active editor tool and hosts the tool factories, which self-register with
/// [Subsystem(nameof(ToolSystem))]. The <see cref="ToolWindow"/> lists them and drives switching; the
/// viewport reads <see cref="Active"/> each frame and delegates interaction to it.
/// </summary>
[SubsystemHost(typeof(IToolFactory))]
public sealed partial class ToolSystem : ISubsystemHost, IWorldParticipant
{
    public ToolContext Context { get; }

    public IToolFactory? ActiveFactory { get; private set; }

    public ITool? Active { get; private set; }

    /// <summary>Every tool factory, in priority order.</summary>
    public IEnumerable<IToolFactory> Factories => Subsystems;

    public ToolSystem(EditorContext context)
    {
        Context = new ToolContext(context, context.Selection, context.Scene, context.EditSessions, context.Axes);
        InitializeSubsystems();
        ActiveFactory ??= Subsystems.FirstOrDefault();
    }

    /// <summary>Creates the default tool if none is active yet.</summary>
    public void EnsureActive()
    {
        if (Active == null && ActiveFactory != null)
        {
            Activate(ActiveFactory);
        }
    }

    public void Activate(IToolFactory factory)
    {
        if (ReferenceEquals(factory, ActiveFactory) && Active != null)
        {
            return;
        }

        Active?.Deactivate();
        ActiveFactory = factory;
        Active = factory.Create(Context);
        Active.Activate();
    }

    /// <summary>Recreates the active tool from the same factory, so a reload doesn't leave a tool
    /// holding references (a paint stroke's target image, say) into a world that just got dropped.
    /// A fresh instance from the same factory is simpler and more robust than auditing every tool's
    /// own fields for what to clear.</summary>
    void IWorldParticipant.UnloadWorld()
    {
        Active?.Deactivate();
        Active = null;
    }

    void IWorldParticipant.LoadWorld() => EnsureActive();
}
