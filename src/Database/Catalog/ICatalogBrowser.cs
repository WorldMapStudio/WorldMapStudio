using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>One search hit: a string key (whatever <see cref="ICatalogBrowser.OpenAsync"/> takes)
/// plus a label to show in the results list.</summary>
public sealed record CatalogSearchResult(string Key, string Label);

/// <summary>
/// Makes a catalog browsable in <see cref="CatalogBrowserWindow"/> — one generic "currently editing a
/// catalog" window instead of a bespoke window per table, since most catalogs want the same
/// search/open/edit shape and, eventually, links between each other.
///
/// A class implementing this already implements <see cref="ICatalogEntityFactory"/> or
/// <see cref="ILazyCatalogEntityFactory"/> for the same catalog — this is a second, additional interface
/// on that same class, not a replacement. <see cref="Handles"/>/<see cref="IEntityFactory.Configure"/>/etc.
/// keep meaning what they already do; this adds only what the browser needs: a display name, a cheap
/// search, opening by a string key (uniform across catalogs even though their real keys differ — <c>byte</c>,
/// <c>uint</c>, composite, ...), and drawing one open entity's fields.
///
/// A catalog that references another (e.g. by a foreign id) draws that as a link in its own
/// <see cref="DrawFields"/>, calling <paramref name="navigate"/> — link-following is each catalog's own
/// responsibility, not the browser's; it alone knows which of its fields are references and to what.
/// </summary>
public interface ICatalogBrowser : IEntityFactory
{
    /// <summary>Display name for the catalog picker, e.g. "Creature Template".</summary>
    string CatalogName { get; }

    /// <summary>A cheap, untracked page of results matching <paramref name="filter"/> (blank filter =
    /// some reasonable default page) — never materializes a full <see cref="CatalogEntity"/>.</summary>
    Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(string filter);

    /// <summary>
    /// Opens the entity for <paramref name="key"/>: if one matching <see cref="Handles"/> is already in
    /// <see cref="CatalogEntityRegistry"/>, returns it as-is; otherwise loads it (whole-catalog lookup or
    /// on-demand fetch, whichever this catalog uses) and adds it. Null if the key doesn't exist. The
    /// uniform string key is what lets <see cref="DrawFields"/>'s <c>navigate</c> callback jump to a
    /// specific row in a different catalog without knowing its real key type.
    /// </summary>
    Task<CatalogEntity?> OpenAsync(EditorContext context, string key);

    /// <summary>
    /// Draws one open entity's fields, recording edits into <paramref name="tracker"/> the same way
    /// every other catalog window does. A field that references another catalog's row draws that as a
    /// link — call <c>navigate(catalogName, key)</c> (matching some other <see cref="ICatalogBrowser.CatalogName"/>
    /// and a key <see cref="OpenAsync"/> on that catalog accepts) to switch the browser there.
    ///
    /// <paramref name="fieldFilter"/> is the browser's live "filter fields" text — route field drawing
    /// through a <see cref="CatalogFieldSheet"/> built with it so a blank filter shows everything and
    /// typed text narrows to matching labels.
    /// </summary>
    void DrawFields(EditorContext context, CatalogEntity entity, FieldEditTracker tracker,
        Action<string, string> navigate, string fieldFilter);

    /// <summary>
    /// Draws this catalog's own "create new" affordance in the search view — typically a single button,
    /// but left to the catalog rather than a generic "New" button the browser draws itself, since some
    /// catalogs need more than a blank row (a catalog spanning several tables might want a name up
    /// front, sensible defaults, ...). Responsible for choosing the key (defaulting it, letting the user
    /// edit it, validating it) and then calling <see cref="Create"/> to do the actual creating, so the
    /// UI and non-UI paths cannot drift — then calling <paramref name="onCreated"/> so the browser
    /// navigates there. A no-op implementation (draw nothing) is fine for a catalog that isn't
    /// creatable yet.
    /// </summary>
    void DrawCreate(EditorContext context, Action<CatalogEntity> onCreated);

    /// <summary>
    /// Creates a new entity for <paramref name="key"/> with this catalog's sensible defaults and adds it
    /// to <see cref="CatalogEntityRegistry"/> through a recorded <c>IEditCommand</c> (a
    /// <see cref="CreateCatalogEntityCommand"/>, typically) so creation is undoable — and persisted on
    /// commit — like every other edit. The UI-free half of <see cref="DrawCreate"/>, which is what makes
    /// any catalog creatable from a script without the scripting layer knowing a thing about which
    /// catalogs exist or what their keys mean. The same uniform string key <see cref="OpenAsync"/> takes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is malformed, already taken, or this catalog
    /// isn't creatable. Loud on purpose: a script asking for a row it can't have is a bug to surface,
    /// not something to silently return null for.</exception>
    CatalogEntity Create(EditorContext context, string key);

    /// <summary>
    /// An unused key for a brand-new row — what the "New" affordance on a link into this catalog hands
    /// to <see cref="Create"/>. Null when this catalog can't pick one unaided (it needs a name, a
    /// composite key, ...), which just hides that affordance. Default: null.
    /// </summary>
    Task<string?> SuggestKeyAsync() => Task.FromResult<string?>(null);
}
