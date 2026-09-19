namespace WorldMapStudio;

/// <summary>
/// Self-registers a group of scripting functionality into <see cref="ScriptingSystem"/>, exposed to
/// JS as <c>wms.&lt;Name&gt;</c>. Declare [Subsystem(nameof(ScriptingSystem))] to register — the same
/// shape <see cref="IToolFactory"/> uses for <see cref="ToolSystem"/>. Its public members marked
/// [ScriptProperty]/[ScriptFunction] are what scripts actually see; everything else on the class is
/// invisible to JS regardless of accessibility.
/// </summary>
public interface IScriptModule : ISubsystem
{
    /// <summary>The name this module is exposed under in JS, e.g. "scene" for <c>wms.scene</c>.</summary>
    string Name { get; }
}
