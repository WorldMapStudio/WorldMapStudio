namespace WorldMapStudio;

/// <summary>A non-fatal issue found while resolving a style. Resolution never throws; a broken
/// file surfaces as one or more of these instead, with the affected value falling back to its
/// parent (or a code-declared default).</summary>
public sealed class StyleProblem
{
    public string Path { get; }
    public string Message { get; }

    public StyleProblem(string path, string message)
    {
        Path = path;
        Message = message;
    }

    public override string ToString() => $"{Path}: {Message}";
}
