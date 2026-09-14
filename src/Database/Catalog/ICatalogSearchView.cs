using System;
using Vector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>Whether a <see cref="ICatalogSearchViewSession"/> is backing a full browser page or a
/// picker popup — the two hosts a view's session may be asked to draw into.</summary>
public enum CatalogSearchPurpose
{
    Browse,
    Pick,
}

/// <summary>
/// What a <see cref="CatalogBrowserWindow"/> or <see cref="CatalogEntityPicker"/> host hands a session on
/// construction: the catalog to query, which host shape it is drawing for, the filter text carried over
/// from whatever session preceded it, and the callbacks a session reports selection through.
/// <see cref="SelectedKey"/>/<see cref="Highlight"/> are meaningless (and unused) for
/// <see cref="CatalogSearchPurpose.Browse"/> — Browse only ever calls <see cref="Activate"/>.
/// </summary>
public sealed record CatalogSearchViewHost(
    EditorContext Context,
    ICatalogBrowser Catalog,
    CatalogSearchPurpose Purpose,
    string InitialFilter,
    Func<string> SelectedKey,
    Action<string> Highlight,
    Action<string> Activate);

/// <summary>
/// One way to search and browse a catalog's rows — the list table every catalog gets by default
/// (<see cref="CatalogListSearchView"/>), or a richer presentation a catalog opts into (a thumbnail
/// gallery). Registered as a self-registering facet (<c>[Subsystem(nameof(CataStorage))]</c>, etc.) and
/// collected through <see cref="Storage.CatalogSearchViews"/> / <see cref="CatalogSearchViews"/>, the
/// same pattern <see cref="ICatalogBrowser"/> itself uses — a catalog never lists its own views, and a
/// view declares which catalogs it supports rather than being named by them.
///
/// A view is a singleton subsystem; it never holds per-open-popup state itself. Each host (the browser
/// window, a Load popup) builds its own <see cref="ICatalogSearchViewSession"/> through
/// <see cref="CreateSession"/> so two open hosts can have independent filters, selections and scroll
/// positions. Expensive shared resources a session's queries need (a baker, a whole-table index) belong
/// on the view or a shared service behind it, never on the session itself — a session that never gets
/// disposed (its host window was simply closed) must not leak anything significant.
/// </summary>
public interface ICatalogSearchView : ISubsystem
{
    /// <summary>Shown in the view toggle, e.g. "List", "Gallery".</summary>
    string ViewName { get; }

    // ISubsystem.Priority doubles as the toggle's own ordering (highest = default when a catalog has
    // no remembered preference) — a subsystem's construction order and a view's place in its own
    // toggle are unrelated concerns, but reusing the member avoids a second, identically-shaped one.
    // CatalogListSearchView returns the lowest value on purpose, so a catalog with an additional view
    // keeps behaving exactly as before until the user opts in.

    /// <summary>Whether this view can present <paramref name="catalog"/>'s rows at all.</summary>
    bool Supports(ICatalogBrowser catalog);

    /// <summary>The size a <see cref="CatalogEntityPicker"/> popup should snap to when this view is the
    /// one showing — a gallery wants far more room than the plain list.</summary>
    Vector2 PreferredPickerSize { get; }

    /// <summary>Builds a fresh session for one open host. The host is responsible for disposing it (on
    /// switching views, on the catalog changing, or on the host itself closing).</summary>
    ICatalogSearchViewSession CreateSession(CatalogSearchViewHost host);
}

/// <summary>
/// One open view's worth of state: the query text, results, selection/scroll position — everything a
/// <see cref="CatalogBrowserWindow"/> or <see cref="CatalogEntityPicker"/> host used to keep for itself
/// before views existed. The host still draws its own chrome (Back, the catalog combo, the view toggle,
/// <see cref="ICatalogBrowser.DrawCreate"/>, a picker's Clear/Select/Cancel footer) — a session only ever
/// fills the results region <see cref="Draw"/> is given.
/// </summary>
public interface ICatalogSearchViewSession : IDisposable
{
    /// <summary>The session's own filter text, carried over from whichever session preceded it when the
    /// user switches views (see <see cref="CatalogSearchViewHost.InitialFilter"/>).</summary>
    string Filter { get; set; }

    /// <summary>Draws the filter box, any view-specific query controls, and the results themselves
    /// within <paramref name="available"/> — never anything the host itself already draws.</summary>
    void Draw(Vector2 available);

    /// <summary>Display label for <paramref name="key"/> if this session's own current results page
    /// already has it cheaply on hand — used for a picker footer's "Selected: …" line. Null (the
    /// default) falls back to the bare key.</summary>
    string? LabelFor(string key) => null;
}
