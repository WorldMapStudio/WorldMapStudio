using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// Per-<see cref="EditorContext"/> cache of catalog reference display text, for widgets that draw a
/// reference field every frame without hitting the database every frame. A miss queues the key;
/// <see cref="Flush"/> — called once per frame by <see cref="Editor.Update"/> — dispatches one
/// <see cref="ICatalogBrowser.DescribeAsync"/> per catalog with anything queued (skipping a catalog
/// already mid-flight) and folds in whatever earlier call has completed since. Never blocks: a widget
/// sees <see cref="LabelState.Pending"/> for a frame or two rather than stalling the draw.
///
/// Wholesale-invalidated on any undo history change (record/undo/redo/commit all bump
/// <see cref="UndoHistory.Revision"/>) rather than tracked per-entity: simpler, and cheap enough since
/// a miss is one query per catalog, not per key.
///
/// Also owns the catalog-name lookup every reference site otherwise repeats as its own private
/// <c>FindCatalog</c>.
/// </summary>
public sealed class CatalogReferenceLabels
{
    public enum LabelState
    {
        Pending,
        Resolved,
        Missing,
    }

    private readonly record struct CacheEntry(LabelState State, string Text);

    private sealed class CatalogCache
    {
        public Dictionary<string, CacheEntry> Entries { get; } = new();
        public HashSet<string> Queued { get; set; } = new();
        public HashSet<string>? InFlightKeys;
        public Task<IReadOnlyDictionary<string, string>>? InFlight;
    }

    private readonly EditorContext _context;
    private readonly Dictionary<string, CatalogCache> _catalogs = new();

    private EditSession? _lastSession;
    private int _lastRevision = -1;

    public CatalogReferenceLabels(EditorContext context) => _context = context;

    /// <summary>The <see cref="ICatalogBrowser"/> named <paramref name="catalogName"/>, or null if none
    /// is registered — the lookup every reference widget and inspector otherwise re-implements.</summary>
    public ICatalogBrowser? FindCatalog(string catalogName) =>
        _context.Database.Storages.SelectMany(storage => storage.CatalogBrowsers)
            .FirstOrDefault(browser => browser.CatalogName == catalogName);

    /// <summary>Cached display text for <paramref name="key"/> in <paramref name="catalogName"/>. A
    /// miss returns <see cref="LabelState.Pending"/> and queues the key for the next <see cref="Flush"/>.
    /// Callers handle an empty <paramref name="key"/> (no reference) themselves — this is only for a
    /// key that names a row.</summary>
    public LabelState TryGet(string catalogName, string key, out string text)
    {
        InvalidateIfStale();

        CatalogCache cache = CacheFor(catalogName);
        if (cache.Entries.TryGetValue(key, out CacheEntry entry))
        {
            text = entry.Text;
            return entry.State;
        }

        cache.Queued.Add(key);
        text = string.Empty;
        return LabelState.Pending;
    }

    /// <summary>Call once per frame (see <see cref="Editor.Update"/>): folds in any completed
    /// <see cref="ICatalogBrowser.DescribeAsync"/> call and starts one for every catalog with queued
    /// misses and nothing already in flight.</summary>
    public void Flush()
    {
        InvalidateIfStale();

        foreach (CatalogCache cache in _catalogs.Values)
        {
            if (cache.InFlight is not { IsCompleted: true } finished)
            {
                continue;
            }

            HashSet<string> requested = cache.InFlightKeys!;
            cache.InFlight = null;
            cache.InFlightKeys = null;

            // A faulted DescribeAsync (a dropped connection, say) leaves the keys uncached rather than
            // caching a failure as Missing — the next TryGet re-queues and retries.
            if (finished.IsCompletedSuccessfully)
            {
                IReadOnlyDictionary<string, string> described = finished.Result;
                foreach (string key in requested)
                {
                    cache.Entries[key] = described.TryGetValue(key, out string? text)
                        ? new CacheEntry(LabelState.Resolved, text)
                        : new CacheEntry(LabelState.Missing, string.Empty);
                }
            }
        }

        foreach ((string catalogName, CatalogCache cache) in _catalogs)
        {
            if (cache.InFlight is not null || cache.Queued.Count == 0)
            {
                continue;
            }

            if (FindCatalog(catalogName) is not { } browser)
            {
                foreach (string key in cache.Queued)
                {
                    cache.Entries[key] = new CacheEntry(LabelState.Missing, string.Empty);
                }

                cache.Queued.Clear();
                continue;
            }

            cache.InFlightKeys = cache.Queued;
            cache.Queued = new HashSet<string>();
            cache.InFlight = browser.DescribeAsync(_context, cache.InFlightKeys);
        }
    }

    private CatalogCache CacheFor(string catalogName)
    {
        if (!_catalogs.TryGetValue(catalogName, out CatalogCache? cache))
        {
            cache = new CatalogCache();
            _catalogs[catalogName] = cache;
        }

        return cache;
    }

    // Session identity, not just the revision counter: EditSessionManager.Commit clears the active
    // session's history (resetting Revision to 0) and then replaces Active outright, so a revision
    // number alone could coincidentally repeat across a commit.
    private void InvalidateIfStale()
    {
        EditSession active = _context.EditSessions.Active;
        int revision = active.History.Revision;
        if (ReferenceEquals(active, _lastSession) && revision == _lastRevision)
        {
            return;
        }

        _lastSession = active;
        _lastRevision = revision;
        _catalogs.Clear();
    }
}
