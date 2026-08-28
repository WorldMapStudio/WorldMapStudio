using System;

namespace WorldMapStudio;

public sealed class ShortcutAction
{
    private readonly Action _execute;
    private readonly Func<bool> _enabled;

    public string Id { get; }
    public string Group { get; }
    public string Name { get; }
    public KeyboardShortcut DefaultShortcut { get; }
    public KeyboardShortcut Shortcut { get; internal set; }

    public bool IsEnabled => _enabled();
    public bool HasCustomShortcut => Shortcut != DefaultShortcut;
    public string ShortcutLabel => Shortcut.DisplayName;
    public string DefaultLabel => DefaultShortcut.DisplayName;

    public ShortcutAction(
        string id,
        string group,
        string name,
        KeyboardShortcut defaultShortcut,
        Action execute,
        Func<bool>? enabled = null)
    {
        Id = id;
        Group = group;
        Name = name;
        DefaultShortcut = defaultShortcut;
        Shortcut = defaultShortcut;
        _execute = execute;
        _enabled = enabled ?? (() => true);
    }

    public void Execute()
    {
        if (IsEnabled)
        {
            _execute();
        }
    }
}
