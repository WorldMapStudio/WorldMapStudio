using System;
using System.Collections.Generic;

namespace WorldMapStudio;

public sealed record PrefabSelectionContext(
    IReadOnlyList<Prefab> Prefabs,
    Action<Prefab> Select,
    Action<Prefab> Delete);
