namespace WorldMapStudio;

[Subsystem(nameof(ViewMenu))]
public sealed class TerrainVertexLightMenuItem(ViewMenu menu) : ViewToggleMenuItem(
    menu, 3f, "view.terrain-vertex-light", "Terrain Vertex Light", KeyboardShortcut.None,
    view => view.ShowTerrainVertexLight, (view, shown) => view.ShowTerrainVertexLight = shown);
