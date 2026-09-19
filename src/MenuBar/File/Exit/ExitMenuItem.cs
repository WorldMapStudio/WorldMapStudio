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

    public void Draw()
    {
        if (ImGui.MenuItem("Exit", _shortcut.ShortcutLabel))
        {
            manager.RequestExit();
        }
    }
}
