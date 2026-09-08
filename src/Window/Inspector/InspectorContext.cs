namespace WorldMapStudio;

/// <summary>Services an <see cref="IEntityInspector"/> needs while drawing, such as recording edits.</summary>
public sealed class InspectorContext(EditSessionManager sessions, FieldFilter fields)
{
    /// <summary>The active edit session to record field edits into.</summary>
    public EditSessionManager Sessions { get; } = sessions;

    /// <summary>The "filter fields" gate for the current frame — wrap each field row in
    /// <see cref="FieldFilter.Field(string, System.Action)"/> so the inspector search box narrows it.</summary>
    public FieldFilter Fields { get; } = fields;
}
