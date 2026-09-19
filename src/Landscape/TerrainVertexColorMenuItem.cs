namespace WorldMapStudio;

[Subsystem(nameof(ViewMenu))]
public sealed class TerrainVertexColorMenuItem(ViewMenu menu) : ViewToggleMenuItem(
    menu, 2f, "view.terrain-vertex-color", "Terrain Vertex Color", KeyboardShortcut.None,
    view => view.ShowTerrainVertexColor, (view, shown) => view.ShowTerrainVertexColor = shown);
