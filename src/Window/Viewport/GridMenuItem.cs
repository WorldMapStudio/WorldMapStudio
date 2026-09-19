using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(ViewMenu))]
public sealed class GridMenuItem(ViewMenu menu) : ViewToggleMenuItem(
    menu, 0f, "view.grid", "Grid", new KeyboardShortcut(ImGuiKey.G, ShortcutModifiers.Alt),
    view => view.ShowGrid, (view, shown) => view.ShowGrid = shown);
