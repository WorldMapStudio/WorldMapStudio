namespace WorldMapStudio;

/// <summary>The editor's console log, exposed to JS as <c>wms.log</c>. Anything written here shows up
/// in the <see cref="LogWindow"/> and the engine output alongside everything else.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class LogScriptApi : IScriptModule
{
    public string Name => "log";

    public LogScriptApi(ScriptingSystem system)
    {
    }

    [ScriptFunction]
    public void Info(string message) => ConsoleLog.Info(message);

    [ScriptFunction]
    public void Warn(string message) => ConsoleLog.Warning(message);

    [ScriptFunction]
    public void Error(string message) => ConsoleLog.Error(message);

    /// <summary>The last <paramref name="count"/> log lines, oldest first.</summary>
    [ScriptFunction]
    public string[] Recent(int count) => ConsoleLog.Recent(count);

    /// <summary>Empties the Log window. The log file on disk is untouched.</summary>
    [ScriptFunction]
    public void Clear() => ConsoleLog.Clear();
}
