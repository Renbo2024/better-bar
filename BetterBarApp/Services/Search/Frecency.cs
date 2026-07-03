using System.IO;
using System.Text.Json;

namespace BetterBarApp.Services.Search;

/// <summary>
/// Per-(source,entry) usage stats producing a ranking nudge (spec §8.5) PLUS a
/// contextual map (spec §8.6): "for the query I typed, which entry did I pick?".
/// When the user repeatedly picks the same result for a given query, that result is
/// pinned to the top for that exact query. Persisted as JSON. Per-source toggle: data
/// for a disabled source is never recorded, consulted, or persisted.
/// </summary>
public sealed class Frecency
{
    private const double HalfLifeDays = 30.0;
    private readonly double _maxBonus;          // global within-tier nudge ceiling

    private sealed class Stat { public int Count { get; set; } public long LastUnix { get; set; } }

    private sealed class Persisted
    {
        public Dictionary<string, Stat> Stats { get; set; } = new();
        // normalized query -> (entryKey -> pick count)
        public Dictionary<string, Dictionary<string, int>> Context { get; set; } = new();
    }

    // Per-owner store: each start button keeps its own recency data so buttons don't
    // pollute each other's rankings.
    private readonly string _filePath;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    private readonly Dictionary<string, Stat> _stats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _context = new(StringComparer.Ordinal);

    // Live predicate: which source ids currently have recency enabled. Evaluated on
    // each call so the per-source toggles can change at runtime. Historical stats are
    // retained in memory for disabled sources, so toggling a source back on restores
    // its data instead of starting from zero.
    private readonly Func<string, bool> _isEnabled;

    public Frecency(Guid ownerId, Func<string, bool> isFrecencyEnabled, double maxBonus)
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BetterBar", $"frecency_{ownerId:N}.json");
        _isEnabled = isFrecencyEnabled;
        _maxBonus = maxBonus;
        Load();
    }

    private static string Key(string sourceId, string id) => sourceId + "|" + id;

    /// <summary>
    /// Recency-weighted usage strength, 0..1 (independent of the bonus ceiling): a recently and/or
    /// repeatedly launched entry trends toward 1, an unused one is 0. Used both for the within-tier
    /// nudge (<see cref="BonusFor"/>) and to decide whether an entry is "popular" enough for the
    /// Popular section. 0 if the entry's source has recency disabled or it was never launched.
    /// </summary>
    public double Strength(SearchEntry entry)
    {
        if (!_isEnabled(entry.SourceId)) return 0;
        if (!_stats.TryGetValue(Key(entry.SourceId, entry.Id), out var s) || s.Count == 0) return 0;

        double days    = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - s.LastUnix) / 86400.0;
        double recency = Math.Pow(0.5, Math.Max(0, days) / HalfLifeDays);
        double raw     = s.Count * recency;
        return raw / (raw + 5.0);
    }

    /// <summary>Global frecency nudge, 0..MaxBonus (reorders within a match tier).</summary>
    public double BonusFor(SearchEntry entry) => _maxBonus * Strength(entry);

    /// <summary>How many times this entry was picked for this exact (normalized) query.
    /// Non-zero pins it above generic ranking (spec §8.6). 0 if none / source disabled.</summary>
    public int ContextCount(string normalizedQuery, SearchEntry entry)
    {
        if (!_isEnabled(entry.SourceId) || string.IsNullOrEmpty(normalizedQuery)) return 0;
        return _context.TryGetValue(normalizedQuery, out var byEntry)
            && byEntry.TryGetValue(Key(entry.SourceId, entry.Id), out var n) ? n : 0;
    }

    public void RecordLaunch(SearchEntry entry, string? queryContext)
    {
        if (!_isEnabled(entry.SourceId)) return;
        var key = Key(entry.SourceId, entry.Id);

        if (!_stats.TryGetValue(key, out var s)) _stats[key] = s = new Stat();
        s.Count++;
        s.LastUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var q = SearchText.Normalize(queryContext);
        if (q.Length > 0)
        {
            if (!_context.TryGetValue(q, out var byEntry)) _context[q] = byEntry = new();
            byEntry[key] = byEntry.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var data = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(_filePath), Opts);
            if (data == null) return;

            // Keep all persisted stats in memory regardless of current toggles; the
            // live predicate decides whether each is consulted. This lets a source be
            // toggled back on without having lost its history.
            foreach (var (key, stat) in data.Stats)
                _stats[key] = stat;

            foreach (var (query, byEntry) in data.Context)
                if (byEntry.Count > 0) _context[query] = new(byEntry);
        }
        catch { /* incompatible/old file → start fresh (usage data is regenerable) */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var data = new Persisted { Stats = _stats, Context = _context };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data, Opts));
        }
        catch { }
    }
}
