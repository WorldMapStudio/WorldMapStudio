using System;

namespace WorldMapStudio;

public sealed record ImageSelectionContext(
    ImageSystem System,
    int? CurrentId,
    Action<int?> Select);
