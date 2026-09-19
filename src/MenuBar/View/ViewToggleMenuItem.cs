using System;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>A checkmarked View menu entry for one <see cref="ViewSettings"/> flag, with its own shortcut.</summary>
public abstract class ViewToggleMenuItem : IMenuItem
{
    private readonly ViewSettings _view;
    private readonly string _label;
    private readonly Func<ViewSettings, bool> _get;
    private readonly Action<ViewSettings, bool> _set;
    private readonly ShortcutAction _shortcut;

    protected ViewToggleMenuItem(
        ViewMenu menu,
        float priority,
        string shortcutId,
        string label,
        KeyboardShortcut defaultShortcut,
        Func<ViewSettings, bool> get,
        Action<ViewSettings, bool> set)
    {
        _view = menu.Context.View;
        _label = label;
        _get = get;
        _set = set;
        Priority = priority;
        _shortcut = menu.Context.Shortcuts.Register(
            shortcutId, "View", label, defaultShortcut, () => _set(_view, !_get(_view)));
    }

    public float Priority { get; }

    public void Draw()
    {
        bool shown = _get(_view);
        if (ImGui.MenuItem(_label, _shortcut.ShortcutLabel, ref shown))
        {
            _set(_view, shown);
        }
    }
}
