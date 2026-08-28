using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(FileMenuManager))]
public sealed class ExitMenuItem(FileMenuManager manager) : IFileMenuItem
{
    private readonly ShortcutAction _shortcut = manager.Shortcuts.Register(
        "file.exit",
        "File",
        "Exit",
        new KeyboardShortcut(ImGuiKey.F4, ShortcutModifiers.Alt),
        manager.RequestExit);

    public float Priority => 0f;

    public void Draw()
    {
        if (ImGui.MenuItem("Exit", _shortcut.ShortcutLabel))
        {
            manager.RequestExit();
        }
    }
}
