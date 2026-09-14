using System;

namespace WorldMapStudio;

/// <summary>Context for one open <see cref="CatalogEntityPicker"/> popup: the editor context (needed to
/// resolve which <see cref="ICatalogSearchView"/>s the catalog supports), which catalog to search,
/// the key currently referenced (blank = none), and the callback a selection reports back through.</summary>
public sealed record CatalogEntityPickerContext(
    EditorContext Context,
    ICatalogBrowser Catalog,
    string CurrentKey,
    Action<string> Select);
