using System;

namespace WorldMapStudio;

public sealed record TextureSelectionContext(
    AssetSystem Assets,
    string CurrentPath,
    Action<string> Select,
    string InitialFilter = "");
