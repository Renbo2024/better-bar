using System.IO;

namespace BetterBarApp.Services.Search;

/// <summary>
/// Per-source change watcher (spec §7). Each source declares its WatchRoots; a change
/// under a root marks just THAT source dirty. A periodic flush (every
/// <see cref="FlushSeconds"/>) re-enumerates only the dirty sources, collapsing bursts
/// into one refresh per source. A buffer-overflow Error is treated as a change (a
/// re-enumeration covers any lost events).
///
/// A configured folder can be offline (drive unplugged / share disconnected) when the
/// index is built: it gets no FileSystemWatcher (there is nothing to watch) and is simply
/// absent from results. A separate availability poll (every <see cref="AvailabilitySeconds"/>)
/// re-checks each source's <see cref="ISearchSource.IsAvailable"/> on a background timer
/// thread; when one toggles it re-enumerates that source (dropping or restoring its entries)
/// and re-attaches/detaches its watcher — all without throwing or blocking the bar.
/// </summary>
internal sealed class SearchWatcher : IDisposable
{
    private const double FlushSeconds = 5;
    private const double AvailabilitySeconds = 30;

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<ISearchSource> _dirty = new();
    private System.Timers.Timer? _flush;
    private System.Timers.Timer? _availability;
    private Action<ISearchSource>? _refresh;
    private List<ISearchSource> _sources = new();
    // Last-seen availability per source, so the poll only re-enumerates on a transition.
    private readonly Dictionary<ISearchSource, bool> _available = new();

    public void Start()
    {
        if (_flush != null) return;
        _flush = new System.Timers.Timer(FlushSeconds * 1000) { AutoReset = true };
        _flush.Elapsed += (_, _) => Flush();
        _flush.Start();

        // Off-UI-thread re-check of offline folders; fires on a ThreadPool timer thread so a
        // slow Directory.Exists over a dead share never stalls the bar.
        _availability = new System.Timers.Timer(AvailabilitySeconds * 1000) { AutoReset = true };
        _availability.Elapsed += (_, _) => PollAvailability();
        _availability.Start();
    }

    /// <summary>Re-creates the watchers for the current source set (called after each build).</summary>
    public void Sync(IEnumerable<ISearchSource> sources, Action<ISearchSource> refresh)
    {
        lock (_gate)
        {
            _refresh = refresh;
            _sources = sources.ToList();
            _available.Clear();
            foreach (var s in _sources) _available[s] = s.IsAvailable;
            RebuildWatchers();
        }
    }

    /// <summary>Builds one FileSystemWatcher per existing local root of every current
    /// source. Caller must hold <see cref="_gate"/>.</summary>
    private void RebuildWatchers()
    {
        foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
        _watchers.Clear();

        foreach (var source in _sources)
        {
            var src = source;   // capture per source
            foreach (var dir in source.WatchRoots
                         .Where(d => !string.IsNullOrWhiteSpace(d))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // FileSystemWatcher is unreliable over SMB/UNC and tends to raise
                // Error storms there — which would needlessly re-enumerate. Skip
                // network paths (rely on manual reload for those).
                if (dir.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var w = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = true,
                        InternalBufferSize    = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        EnableRaisingEvents   = true,
                    };
                    w.Created += (_, _) => MarkDirty(src);
                    w.Deleted += (_, _) => MarkDirty(src);
                    w.Changed += (_, _) => MarkDirty(src);
                    w.Renamed += (_, _) => MarkDirty(src);
                    w.Error   += (_, _) => MarkDirty(src);
                    _watchers.Add(w);
                }
                catch { /* inaccessible dir → skip */ }
            }
        }
    }

    // Re-checks every source's availability. A source that came back online is re-enumerated
    // (and re-watched); one that just dropped offline is re-enumerated too (its now-empty
    // result clears its stale entries). IsAvailable is probed OUTSIDE the lock so a blocking
    // Directory.Exists on a dead path can't hold up flushes or Sync.
    private void PollAvailability()
    {
        ISearchSource[] sources;
        lock (_gate) sources = _sources.ToArray();
        if (sources.Length == 0) return;

        var changed = new List<ISearchSource>();
        foreach (var source in sources)
        {
            bool now = source.IsAvailable;
            lock (_gate)
            {
                if (!_sources.Contains(source)) continue;          // superseded by a rebuild
                if (!_available.TryGetValue(source, out var was)) was = true;
                if (now == was) continue;
                _available[source] = now;
            }
            changed.Add(source);
        }
        if (changed.Count == 0) return;

        Action<ISearchSource>? refresh;
        lock (_gate)
        {
            RebuildWatchers();   // attach watchers to roots that reappeared / drop vanished ones
            refresh = _refresh;
        }
        if (refresh == null) return;
        foreach (var source in changed) refresh(source);   // fire-and-forget re-enumerate
    }

    private void MarkDirty(ISearchSource source)
    {
        lock (_gate) _dirty.Add(source);
    }

    private void Flush()
    {
        ISearchSource[] due;
        Action<ISearchSource>? refresh;
        lock (_gate)
        {
            if (_dirty.Count == 0) return;
            due = _dirty.ToArray();
            _dirty.Clear();
            refresh = _refresh;
        }
        if (refresh == null) return;
        foreach (var source in due) refresh(source);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _flush?.Stop();
            _flush?.Dispose();
            _flush = null;
            _availability?.Stop();
            _availability?.Dispose();
            _availability = null;
            foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
            _watchers.Clear();
            _refresh = null;
        }
    }
}
