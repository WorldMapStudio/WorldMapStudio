using System;
using ImGuiNET;

namespace WorldMapStudio;

public readonly record struct KeyboardShortcut(ImGuiKey Key, ShortcutModifiers Modifiers)
{
    public bool IsBound => Key != ImGuiKey.None;

    public string DisplayName
    {
        get
        {
            if (!IsBound)
            {
                return string.Empty;
            }

            string prefix = string.Empty;
            if (Modifiers.HasFlag(ShortcutModifiers.Ctrl)) { prefix += "Ctrl+"; }
            if (Modifiers.HasFlag(ShortcutModifiers.Alt)) { prefix += "Alt+"; }
            if (Modifiers.HasFlag(ShortcutModifiers.Shift)) { prefix += "Shift+"; }
            if (Modifiers.HasFlag(ShortcutModifiers.Super)) { prefix += "Super+"; }
            return prefix + KeyName(Key);
        }
    }

    public static KeyboardShortcut None => new(ImGuiKey.None, ShortcutModifiers.None);

    public bool IsPressed() => IsPressed(ImGui.GetIO());

    // Key test first: the no-op path (no key down this frame) then costs one check instead of a
    // marshalled GetIO() plus modifier compare per action per frame.
    public bool IsPressed(ImGuiIOPtr io)
    {
        if (!IsBound || !ImGui.IsKeyPressed(Key, false))
        {
            return false;
        }

        return ModifiersMatch(io);
    }

    public bool ConflictsWith(KeyboardShortcut other) =>
        IsBound && other.IsBound && Key == other.Key && Modifiers == other.Modifiers;

    public static bool TryParse(string value, out KeyboardShortcut shortcut)
    {
        shortcut = None;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ShortcutModifiers modifiers = ShortcutModifiers.None;
        string keyName = string.Empty;
        foreach (string rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawPart.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ShortcutModifiers.Ctrl;
            }
            else if (rawPart.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ShortcutModifiers.Alt;
            }
            else if (rawPart.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ShortcutModifiers.Shift;
            }
            else if (rawPart.Equals("Super", StringComparison.OrdinalIgnoreCase)
                     || rawPart.Equals("Meta", StringComparison.OrdinalIgnoreCase)
                     || rawPart.Equals("Win", StringComparison.OrdinalIgnoreCase)
                     || rawPart.Equals("Cmd", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ShortcutModifiers.Super;
            }
            else
            {
                keyName = rawPart;
            }
        }

        if (string.IsNullOrWhiteSpace(keyName) || !TryParseKey(keyName, out ImGuiKey key))
        {
            return false;
        }

        shortcut = new KeyboardShortcut(key, modifiers);
        return true;
    }

    public static ShortcutModifiers CurrentModifiers(ImGuiIOPtr io)
    {
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        if (io.KeyCtrl) { modifiers |= ShortcutModifiers.Ctrl; }
        if (io.KeyAlt) { modifiers |= ShortcutModifiers.Alt; }
        if (io.KeyShift) { modifiers |= ShortcutModifiers.Shift; }
        if (io.KeySuper) { modifiers |= ShortcutModifiers.Super; }
        return modifiers;
    }

    public static string KeyName(ImGuiKey key)
    {
        if (key >= ImGuiKey.A && key <= ImGuiKey.Z)
        {
            return key.ToString();
        }

        if (key >= ImGuiKey._0 && key <= ImGuiKey._9)
        {
            return ((int)(key - ImGuiKey._0)).ToString();
        }

        return key switch
        {
            ImGuiKey.LeftArrow => "Left",
            ImGuiKey.RightArrow => "Right",
            ImGuiKey.UpArrow => "Up",
            ImGuiKey.DownArrow => "Down",
            ImGuiKey.PageUp => "PageUp",
            ImGuiKey.PageDown => "PageDown",
            ImGuiKey.Escape => "Esc",
            ImGuiKey.Space => "Space",
            ImGuiKey.Enter => "Enter",
            ImGuiKey.Delete => "Delete",
            ImGuiKey.Backspace => "Backspace",
            ImGuiKey.Tab => "Tab",
            >= ImGuiKey.F1 and <= ImGuiKey.F12 => key.ToString(),
            _ => key.ToString(),
        };
    }

    private bool ModifiersMatch(ImGuiIOPtr io) => CurrentModifiers(io) == Modifiers;

    private static bool TryParseKey(string name, out ImGuiKey key)
    {
        if (name.Length == 1)
        {
            char ch = char.ToUpperInvariant(name[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = ImGuiKey.A + (ch - 'A');
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                key = ImGuiKey._0 + (ch - '0');
                return true;
            }
        }

        key = name.ToLowerInvariant() switch
        {
            "left" => ImGuiKey.LeftArrow,
            "right" => ImGuiKey.RightArrow,
            "up" => ImGuiKey.UpArrow,
            "down" => ImGuiKey.DownArrow,
            "pageup" => ImGuiKey.PageUp,
            "pagedown" => ImGuiKey.PageDown,
            "esc" or "escape" => ImGuiKey.Escape,
            "space" => ImGuiKey.Space,
            "enter" => ImGuiKey.Enter,
            "delete" or "del" => ImGuiKey.Delete,
            "backspace" => ImGuiKey.Backspace,
            "tab" => ImGuiKey.Tab,
            "f1" => ImGuiKey.F1,
            "f2" => ImGuiKey.F2,
            "f3" => ImGuiKey.F3,
            "f4" => ImGuiKey.F4,
            "f5" => ImGuiKey.F5,
            "f6" => ImGuiKey.F6,
            "f7" => ImGuiKey.F7,
            "f8" => ImGuiKey.F8,
            "f9" => ImGuiKey.F9,
            "f10" => ImGuiKey.F10,
            "f11" => ImGuiKey.F11,
            "f12" => ImGuiKey.F12,
            _ => ImGuiKey.None,
        };

        return key != ImGuiKey.None;
    }
}
