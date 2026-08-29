using System;
using Godot;

namespace WorldMapStudio;

public sealed record ProceduralModelSelectionContext(
    ProceduralMeshSystem System,
    Node PreviewOwner,
    int? CurrentId,
    Action<int?> Select);
