using System;

namespace WorldMapStudio;

/// <summary>Context for one open <see cref="CatalogEntityPicker"/> popup: which catalog to search,
/// the key currently referenced (blank = none), and the callback a selection reports back through.</summary>
public sealed record CatalogEntityPickerContext(
    ICatalogBrowser Catalog,
    string CurrentKey,
    Action<string> Select);
