using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(FileMenuManager))]
public sealed class ExitMenuItem(FileMenuManager manager) : IFileMenuItem
{
    public float Priority => 0f;

    public void Draw()
    {
        if (ImGui.MenuItem("Exit"))
        {
            manager.RequestExit();
        }
    }
}
