using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(ViewMenu))]
public sealed class ChunkEdgesMenuItem(ViewMenu menu) : ViewToggleMenuItem(
    menu, 1f, "view.chunk-edges", "Chunk Edges", new KeyboardShortcut(ImGuiKey.E, ShortcutModifiers.Alt),
    view => view.ShowChunkEdges, (view, shown) => view.ShowChunkEdges = shown);
