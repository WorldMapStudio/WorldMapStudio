using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public abstract class Window : ISubsystem
{
    public string Title { get; }
    public bool IsOpen { get; set; }

    public virtual float Priority => 0f;

    /// <summary>
    /// Optional submenu name to nest this window's "Window" menu entry under. Windows that leave this
    /// null stay at the top level of the menu.
    /// </summary>
    public virtual string? Category => null;

    /// <summary>Default Alt+key shortcut to toggle this window, registered by <see cref="WindowManager"/>.
    /// Windows that leave this as <see cref="KeyboardShortcut.None"/> get no default binding.</summary>
    public virtual KeyboardShortcut DefaultShortcut => KeyboardShortcut.None;

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

    /// <summary>Runs every frame before drawing, even while closed. Lets a window open itself on demand.</summary>
    protected virtual void OnBeforeDraw() { }

    public void Draw()
    {
        OnBeforeDraw();

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

    public void DrawMenuItem(string? shortcut = null)
    {
        bool isOpen = IsOpen;
        if (ImGui.MenuItem(Title, shortcut, ref isOpen))
        {
            IsOpen = isOpen;
        }
    }

    protected abstract void DrawContent();
}
