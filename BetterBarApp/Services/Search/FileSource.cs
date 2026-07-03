using System.IO;
using System.Text.RegularExpressions;

namespace BetterBarApp.Services.Search;

/// <summary>
/// File-backed source (spec §4.3/§4.4): walks a root, applying the regex filter and
/// caps DURING the crawl. Executable extensions are tagged (caution glyph) rather
/// than excluded. Used for Documents and for user-configured locations.
/// </summary>
public sealed class FileSource : ISearchSource
{
    private static readonly HashSet<string> ExecExts = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".msi", ".scr", ".com" };

    private static readonly HashSet<string> ExcludeDirs = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "$RECYCLE.BIN", "obj", "bin" };

    private const int MaxDepth = 8, MaxEntries = 50_000;

    public string SourceId    { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> WatchRoots => new[] { _root };

    /// <summary>The configured root currently exists (resolving a mapped drive to UNC the
    /// same way the crawl does). When false the owning service skips this source rather than
    /// crawling a missing folder, and the availability poll re-checks it every 30s.</summary>
    public bool IsAvailable
    {
        get
        {
            var path = PathUtil.ToUnc(_root);
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }
    }

    private readonly string    _root;
    private readonly bool      _cascade;
    private readonly EntryKind _defaultKind;
    private readonly Regex?    _include;   // if set, only matching file names are indexed
    private readonly Regex?    _exclude;   // if set, matching file names are dropped

    public FileSource(string sourceId, string displayName, string root, bool cascade,
                      EntryKind defaultKind, string? includeRegex = null, string? excludeRegex = null)
    {
        SourceId     = sourceId;
        DisplayName  = displayName;
        _root        = root;
        _cascade     = cascade;
        _defaultKind = defaultKind;
        _include     = Compile(includeRegex);
        _exclude     = Compile(excludeRegex);
    }

    // Bad patterns are ignored (treated as "no filter") so a typo can't blank a folder.
    private static Regex? Compile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
        catch { return null; }
    }

    public Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct) =>
        Task.Run(() => Enumerate(ct), ct);

    private IReadOnlyList<SearchEntry> Enumerate(CancellationToken ct)
    {
        var list = new List<SearchEntry>();
        // Resolve mapped drive letters to UNC so the folder is readable even when
        // BetterBar runs elevated (and so older Z:\-style entries still work).
        var rootPath = PathUtil.ToUnc(_root);
        if (string.IsNullOrWhiteSpace(rootPath)) return list;
        // Missing root (drive unplugged / share offline). The service gates on IsAvailable
        // and normally never calls us in this state; reaching here means it vanished mid-
        // crawl. Return nothing (no exception) — the source is simply absent from results
        // until the availability poll sees it return and re-enumerates it.
        if (!Directory.Exists(rootPath)) return list;

        // Refuse to index a drive root (spec §4.3 — avoid "indexing the world").
        var full = Path.GetFullPath(rootPath);
        if (string.Equals(Path.GetPathRoot(full), full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                          StringComparison.OrdinalIgnoreCase))
            return list;

        Walk(full, 0, list, ct);
        return list;
    }

    private void Walk(string dir, int depth, List<SearchEntry> list, CancellationToken ct)
    {
        // On timeout, stop gracefully and keep what we have (slow network shares
        // still contribute partial results) rather than discarding the source.
        if (list.Count >= MaxEntries || ct.IsCancellationRequested) return;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { return; }
        foreach (var path in files)
        {
            if (list.Count >= MaxEntries) return;
            var fileName = Path.GetFileName(path);
            if (_include != null && !_include.IsMatch(fileName)) continue;  // "only include"
            if (_exclude != null &&  _exclude.IsMatch(fileName)) continue;  // "always exclude"
            if (IconSidecar.IsSidecar(path)) continue;

            var kind = ExecExts.Contains(Path.GetExtension(path)) ? EntryKind.ExecutableFile : _defaultKind;
            list.Add(SearchEntry.Create(path, Path.GetFileNameWithoutExtension(path), SourceId, kind, path));
        }

        if (!_cascade || depth >= MaxDepth) return;

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(dir); } catch { return; }
        foreach (var sub in dirs)
        {
            if (ExcludeDirs.Contains(Path.GetFileName(sub))) continue;
            Walk(sub, depth + 1, list, ct);
        }
    }
}
