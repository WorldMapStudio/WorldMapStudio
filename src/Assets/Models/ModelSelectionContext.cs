using System;
using Godot;

namespace WorldMapStudio;

public sealed record ModelSelectionContext(
    AssetSystem Assets,
    Node PreviewOwner,
    string CurrentPath,
    Action<string> Select);
