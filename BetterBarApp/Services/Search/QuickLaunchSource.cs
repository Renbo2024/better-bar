using System.IO;
using BetterBarApp.Models;

namespace BetterBarApp.Services.Search;

/// <summary>
/// Aggregates the items shown in start-button icon lists and Launcher items across
/// all bar definitions, so the user's own curated launchers are searchable too.
/// Honors the same .bbr hide/name sidecars and excludes the sidecars themselves.
/// </summary>
public sealed class QuickLaunchSource : ISearchSource
{
    public string SourceId    => "quicklaunch";   // internal key (persisted) — keep as-is
    public string DisplayName => "Launcher";       // section header shown in search results
    public IReadOnlyList<string> WatchRoots => _directories().ToList();

    // Reads PanelManager definitions, so the index must be built after settings load.
    private readonly Func<IEnumerable<string>> _directories;

    public QuickLaunchSource(Func<IEnumerable<string>> directories) => _directories = directories;

    public Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct) =>
        Task.Run(() => Enumerate(ct), ct);

    private IReadOnlyList<SearchEntry> Enumerate(CancellationToken ct)
    {
        var list = new List<SearchEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // dedup by full path

        foreach (var dir in _directories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

            IEnumerable<string> files;
            try { files = Directory.GetFiles(dir); } catch { continue; }

            foreach (var path in files)
            {
                if (IconSidecar.IsSidecar(path) || IconSidecar.IsHidden(path)) continue;
                if (!seen.Add(path)) continue;

                var name = IconSidecar.GetName(path) ?? Path.GetFileNameWithoutExtension(path);
                list.Add(SearchEntry.Create(path, name, SourceId, EntryKind.QuickLaunch, path));
            }
        }
        return list;
    }

    /// <summary>Every directory feeding a start-button icon list or a Launcher item.</summary>
    public static IEnumerable<string> ConfiguredDirectories()
    {
        foreach (var def in PanelManager.Definitions)
            foreach (var item in def.Items)
                switch (item)
                {
                    case StartButtonItem sb when !string.IsNullOrWhiteSpace(sb.SourceDirectory):
                        yield return sb.SourceDirectory; break;
                    case LauncherItem l when !string.IsNullOrWhiteSpace(l.SourceDirectory):
                        yield return l.SourceDirectory; break;
                }
    }
}
