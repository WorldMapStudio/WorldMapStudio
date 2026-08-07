namespace WorldMapStudio;

/// <summary>Services an <see cref="IEntityInspector"/> needs while drawing, such as recording edits.</summary>
public sealed class InspectorContext(EditSessionManager sessions)
{
    /// <summary>The active edit session to record field edits into.</summary>
    public EditSessionManager Sessions { get; } = sessions;
}
