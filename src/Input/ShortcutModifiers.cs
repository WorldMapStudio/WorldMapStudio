using System;

namespace WorldMapStudio;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Super = 1 << 3,
}
