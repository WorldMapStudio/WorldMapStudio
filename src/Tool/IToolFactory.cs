namespace WorldMapStudio;

/// <summary>
/// Self-registers a tool with the <see cref="ToolSystem"/> and creates it on demand. Declare
/// [Subsystem(nameof(ToolSystem))] to register.
/// </summary>
public interface IToolFactory : ISubsystem
{
    string Name { get; }

    /// <summary>Whether the tool may currently be selected.</summary>
    bool CanActivate() => true;

    ITool Create(ToolContext context);
}
