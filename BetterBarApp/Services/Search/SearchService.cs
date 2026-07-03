using BetterBarApp.Models;

namespace BetterBarApp.Services.Search;

/// <summary>Immutable published snapshot of the index (spec §3.2).</summary>
public sealed class IndexSnapshot
{
    public IReadOnlyList<SearchEntry>   Entries { get; }
    public IReadOnlyList<ISearchSource> Sources { get; }   // for section order + titles
    public long Version { get; }

    public IndexSnapshot(IReadOnlyList<SearchEntry> entries, IReadOnlyList<ISearchSource> sources, long version)
    {
        Entries = entries; Sources = sources; Version = version;
    }
}

/// <summary>
/// A search index PRIVATE to one <see cref="StartButtonItem"/>. Each start button owns
/// its own definition (which built-in sources, which custom folders, per-source recency)
/// and its own engine + recency store, so two buttons share nothing. Builds in the
/// background (spec §10.1) with a build-then-swap model: readers take the current
/// snapshot reference without locking; the builder publishes a new one atomically.
/// Only the sources the button ENABLES are built, so an apps-only button never crawls
/// folders and a single-folder button never enumerates the shell.
/// </summary>
public sealed class StartSearchService : IDisposable
{
    private readonly StartButtonItem _item;
    private readonly Frecency        _frecency;
    private readonly SearchEngine    _engine;
    private readonly SearchWatcher   _watcher = new();

    public StartSearchService(StartButtonItem item)
    {
        _item = item;
        var w = AppPrefs.SearchWeights;   // shared scorer weights (advanced)
        _frecency = new Frecency(item.Id, IsFrecencyEnabled, w.FrecencyMaxBonus);
        _engine   = new SearchEngine(_frecency, w);
    }

    /// <summary>Live per-source recency toggle (all default off), read from this button's
    /// config so the settings UI can flip a source without a rebuild.</summary>
    private bool IsFrecencyEnabled(string sourceId) => sourceId switch
    {
        "quicklaunch" => _item.FrecencyQuickLaunch,
        "apps"        => _item.FrecencyApps,
        "settings"    => _item.FrecencySettings,
        "documents"   => _item.FrecencyDocuments,
        _ when sourceId.StartsWith("loc:", StringComparison.Ordinal) =>
            _item.SearchLocations.FirstOrDefault(l => LocationSourceId(l.Path) == sourceId)?.Frecency ?? false,
        _ => false,
    };

    /// <summary>Builds this button's source set — only the sources it enables.</summary>
    private List<ISearchSource> BuildSources()
    {
        var sources = new List<ISearchSource>();
        if (_item.SearchQuickLaunch) sources.Add(new QuickLaunchSource(QuickLaunchSource.ConfiguredDirectories));
        if (_item.SearchApps)        sources.Add(new AppsSource());
        if (_item.SearchSettings)    sources.Add(new SettingsSource());

        if (_item.SearchDocuments)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                sources.Add(new FileSource("documents", "Documents", docs, cascade: true, EntryKind.Document));
        }

        sources.AddRange(BuildLocationSources());
        return sources;
    }

    /// <summary>Just this button's custom-folder sources (name, cascade, include/exclude
    /// regex). Reused by <see cref="ReloadLocations"/>.</summary>
    private List<ISearchSource> BuildLocationSources()
    {
        var list = new List<ISearchSource>();
        foreach (var loc in _item.SearchLocations)
        {
            if (string.IsNullOrWhiteSpace(loc.Path)) continue;
            var name = string.IsNullOrWhiteSpace(loc.Name)
                ? System.IO.Path.GetFileName(loc.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
                : loc.Name;
            list.Add(new FileSource(
                LocationSourceId(loc.Path), name, loc.Path, loc.Cascade, EntryKind.UserLocationItem,
                includeRegex: loc.IncludeRegex, excludeRegex: loc.ExcludeRegex));
        }
        return list;
    }

    public static string LocationSourceId(string path) => "loc:" + path;

    private volatile IndexSnapshot? _snapshot;
    private long _version;
    private int  _buildStarted;   // 0/1 guard so we build at most once on demand

    private readonly object _gate = new();
    private List<ISearchSource> _sources = new();
    private readonly Dictionary<string, IReadOnlyList<SearchEntry>> _cache = new();

    /// <summary>Raised (on a background thread) when a new snapshot is published.</summary>
    public event Action? SnapshotChanged;

    public bool IsReady => _snapshot != null;

    /// <summary>Kicks off the one-time background build (and starts live watching).</summary>
    public void EnsureBuilding()
    {
        if (Interlocked.Exchange(ref _buildStarted, 1) == 0)
        {
            _watcher.Start();
            _ = BuildAsync();
        }
    }

    /// <summary>Full re-enumeration of every source (manual reload / config change).</summary>
    public void Reload()
    {
        // If we were never built (button never opened), a build does the same job.
        if (Interlocked.Exchange(ref _buildStarted, 1) == 0) _watcher.Start();
        _ = BuildAsync();
    }

    /// <summary>
    /// Rebuilds only the custom-folder sources from this button's current config,
    /// keeping the (expensive) shell sources — apps, settings, quick launch — and
    /// Documents cached. Used when a folder's name / cascade / include-exclude regex
    /// changes so only that folder's source reloads.
    /// </summary>
    public void ReloadLocations() => _ = ReloadLocationsAsync();

    private async Task ReloadLocationsAsync()
    {
        List<ISearchSource> newSources;
        List<ISearchSource> locSources;
        lock (_gate)
        {
            var nonLoc = _sources.Where(s => !s.SourceId.StartsWith("loc:", StringComparison.Ordinal)).ToList();
            locSources = BuildLocationSources();
            newSources = nonLoc.Concat(locSources).ToList();

            var keep = new HashSet<string>(newSources.Select(s => s.SourceId));
            foreach (var key in _cache.Keys.Where(k => !keep.Contains(k)).ToList()) _cache.Remove(key);
            _sources = newSources;
        }

        foreach (var s in locSources)
        {
            var entries = await EnumerateSafe(s).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_sources.Contains(s)) continue;            // superseded by another reload
                _cache[s.SourceId] = entries ?? Array.Empty<SearchEntry>();
            }
        }
        Publish();
        _watcher.Sync(_sources, source => _ = RefreshSourceAsync(source));
    }

    private async Task BuildAsync()
    {
        var sources = BuildSources();
        var results = new Dictionary<string, IReadOnlyList<SearchEntry>>();
        foreach (var source in sources)
            results[source.SourceId] = await EnumerateSafe(source).ConfigureAwait(false)
                                       ?? Array.Empty<SearchEntry>();

        lock (_gate)
        {
            _sources = sources;
            _cache.Clear();
            foreach (var kv in results) _cache[kv.Key] = kv.Value;
        }
        Publish();

        // Per-source change pipeline: watch each source's roots; a change re-enumerates
        // only that source (see RefreshSourceAsync).
        _watcher.Sync(sources, source => _ = RefreshSourceAsync(source));
    }

    /// <summary>Re-enumerates a single source and republishes — the per-source path.</summary>
    private async Task RefreshSourceAsync(ISearchSource source)
    {
        var entries = await EnumerateSafe(source).ConfigureAwait(false);
        if (entries == null) return;   // transient failure → keep the previous cache
        lock (_gate)
        {
            if (!_sources.Contains(source)) return;   // superseded by a full rebuild
            _cache[source.SourceId] = entries;
        }
        Publish();
    }

    // Source isolation + a generous per-source timeout. Returns null on a TRANSIENT failure
    // (exception / timeout) so callers keep the previous results instead of wiping them; an
    // empty (non-null) list means "enumerated, genuinely 0". An OFFLINE source (a configured
    // folder whose drive/share is currently gone) returns empty — it is dropped from results,
    // without throwing, until the watcher's availability poll sees it return.
    private static async Task<IReadOnlyList<SearchEntry>?> EnumerateSafe(ISearchSource source)
    {
        if (!source.IsAvailable) return Array.Empty<SearchEntry>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await source.EnumerateAsync(cts.Token).ConfigureAwait(false);
        }
        catch { return null; }
    }

    // Build-then-swap: assemble the full entry list from the per-source cache in source
    // order and publish atomically.
    private void Publish()
    {
        List<ISearchSource> sources;
        var all = new List<SearchEntry>();
        lock (_gate)
        {
            sources = _sources.ToList();
            foreach (var s in sources)
                if (_cache.TryGetValue(s.SourceId, out var entries)) all.AddRange(entries);
        }
        _snapshot = new IndexSnapshot(all, sources, Interlocked.Increment(ref _version));
        SnapshotChanged?.Invoke();
    }

    /// <summary>
    /// Runs a query against this button's snapshot (every built source is in scope —
    /// the button only builds the sources it enabled). Returns null if the index isn't
    /// ready yet (the UI shows a loading state); empty list means ready but no matches.
    /// </summary>
    public IReadOnlyList<SearchSection>? Search(string query, int maxPerSection, int maxGlobal)
    {
        var snap = _snapshot;
        if (snap == null) return null;
        var enabled = snap.Sources.Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        return _engine.Search(query, snap, enabled, maxPerSection, maxGlobal, NormalizedAliases());
    }

    // This button's aliases with normalized keys (so they match the engine's normalized query).
    // Read live from the item, so edits in settings apply on the next keystroke without a rebuild.
    private IReadOnlyDictionary<string, string>? NormalizedAliases()
    {
        if (_item.SearchAliases.Count == 0) return null;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (alias, expansion) in _item.SearchAliases)
        {
            var key = SearchText.Normalize(alias);
            if (key.Length > 0 && !string.IsNullOrWhiteSpace(expansion)) d[key] = expansion;
        }
        return d.Count > 0 ? d : null;
    }

    /// <summary>Records a launch; <paramref name="queryContext"/> is the text the user
    /// had typed, used to learn per-query preferences (contextual frecency).</summary>
    public void RecordLaunch(SearchEntry entry, string? queryContext = null) =>
        _frecency.RecordLaunch(entry, queryContext);

    /// <summary>Number of entries currently indexed for a source, or null if it hasn't
    /// been enumerated yet. Used by the settings UI to report per-folder results.</summary>
    public int? CountFor(string sourceId)
    {
        lock (_gate)
            return _cache.TryGetValue(sourceId, out var entries) ? entries.Count : null;
    }

    public void Dispose() => _watcher.Dispose();
}

/// <summary>
/// Process-wide registry of per-button search engines, keyed by <see cref="StartButtonItem.Id"/>.
/// A definition shared across monitors reuses the same item instance → one engine. Engines
/// build in the background and stay warm; settings changes reload the affected engine.
/// </summary>
public static class StartSearch
{
    private static readonly object _gate = new();
    private static readonly Dictionary<Guid, StartSearchService> _engines = new();

    /// <summary>The engine for a button, creating it on first use.</summary>
    public static StartSearchService For(StartButtonItem item)
    {
        lock (_gate)
        {
            if (!_engines.TryGetValue(item.Id, out var svc))
                _engines[item.Id] = svc = new StartSearchService(item);
            return svc;
        }
    }

    /// <summary>Creates (if needed) and starts building the engine for a button.</summary>
    public static void EnsureBuilt(StartButtonItem item) => For(item).EnsureBuilding();

    /// <summary>Full rebuild after a config change (sources toggled, weights, etc.).</summary>
    public static void Reload(StartButtonItem item) => For(item).Reload();

    /// <summary>Reload just the custom folders for a button.</summary>
    public static void ReloadLocations(StartButtonItem item) => For(item).ReloadLocations();
}
