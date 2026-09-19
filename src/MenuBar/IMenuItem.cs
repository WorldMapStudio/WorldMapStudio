using System.Collections.Generic;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>One entry in a hosted menu. Declares [Subsystem(nameof(SomeMenu))] to appear there.</summary>
public interface IMenuItem : ISubsystem
{
    /// <summary>Items are separated where this changes, in Priority order.</summary>
    int Section => 0;

    void Draw();

    /// <summary>Root-level popups this item opens. See <see cref="IMainMenu.DrawOverlay"/>.</summary>
    void DrawOverlay() { }
}

/// <summary>Draws a menu's <see cref="IMenuItem"/>s.</summary>
public static class MenuItems
{
    /// <summary>Draws <paramref name="items"/> in order, with a separator wherever the section changes.</summary>
    public static void Draw(IEnumerable<IMenuItem> items)
    {
        int? section = null;
        foreach (IMenuItem item in items)
        {
            if (section is { } previous && previous != item.Section)
            {
                ImGui.Separator();
            }

            section = item.Section;
            item.Draw();
        }
    }

    public static void DrawOverlay(IEnumerable<IMenuItem> items)
    {
        foreach (IMenuItem item in items)
        {
            item.DrawOverlay();
        }
    }
}
