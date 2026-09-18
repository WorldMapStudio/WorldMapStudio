namespace WorldMapStudio;

/// <summary>What the process reports to its parent when the app quits. Static so any system that ends
/// the session (a script, a menu) can set it without changing what an <see cref="IScene"/> returns.</summary>
public static class AppExit
{
    public static int Code { get; set; }
}
