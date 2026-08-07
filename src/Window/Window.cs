using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public abstract class Window : ISubsystem
{
    public string Title { get; }
    public bool IsOpen { get; set; }

    public virtual float Priority => 0f;

    private readonly Vector2? _defaultSize;
    private readonly Vector2? _defaultPosition;

    protected Window(string title, bool startOpen = true, Vector2? defaultSize = null, Vector2? defaultPosition = null)
    {
        Title = title;
        IsOpen = startOpen;
        _defaultSize = defaultSize;
        _defaultPosition = defaultPosition;
    }

    protected virtual ImGuiWindowFlags Flags => ImGuiWindowFlags.None;

    public void Draw()
    {
        if (!IsOpen)
        {
            return;
        }

        if (_defaultSize.HasValue)
        {
            ImGui.SetNextWindowSize(_defaultSize.Value, ImGuiCond.FirstUseEver);
        }

        if (_defaultPosition.HasValue)
        {
            ImGui.SetNextWindowPos(_defaultPosition.Value, ImGuiCond.FirstUseEver);
        }

        bool isOpen = IsOpen;
        if (ImGui.Begin(Title, ref isOpen, Flags))
        {
            DrawContent();
        }
        ImGui.End();
        IsOpen = isOpen;
    }

    public void DrawMenuItem()
    {
        bool isOpen = IsOpen;
        if (ImGui.MenuItem(Title, null, ref isOpen))
        {
            IsOpen = isOpen;
        }
    }

    protected abstract void DrawContent();
}
