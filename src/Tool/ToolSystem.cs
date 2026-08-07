using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Owns the active editor tool and the registered tool factories. The <see cref="ToolWindow"/>
/// registers its factories here and drives switching; the viewport reads <see cref="Active"/> each
/// frame and delegates interaction to it.
/// </summary>
public sealed class ToolSystem
{
    private readonly List<IToolFactory> _factories = [];

    public ToolContext Context { get; }

    public IToolFactory? ActiveFactory { get; private set; }

    public ITool? Active { get; private set; }

    public IReadOnlyList<IToolFactory> Factories => _factories;

    public ToolSystem(EditorContext context)
    {
        Context = new ToolContext(context.Selection, context.Scene, context.EditSessions, context.Axes);
    }

    public void Register(IToolFactory factory)
    {
        _factories.Add(factory);
        ActiveFactory ??= factory;
    }

    /// <summary>Creates the default tool if none is active yet. Call once factories are registered.</summary>
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
}
