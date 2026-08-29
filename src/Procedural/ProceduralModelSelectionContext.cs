using System;
using Godot;

namespace WorldMapStudio;

public sealed record ProceduralModelSelectionContext(
    ProceduralSystem System,
    Node PreviewOwner,
    int? CurrentId,
    Action<int?> Select);
