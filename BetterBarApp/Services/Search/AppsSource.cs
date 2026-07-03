namespace BetterBarApp.Services.Search;

/// <summary>
/// Apps source: the merged shell AppsFolder (Win32 + Store apps), exactly what
/// Explorer's "All apps" shows — enumerated, not curated (spec §4.1).
/// </summary>
public sealed class AppsSource : ISearchSource
{
    public string SourceId    => "apps";
    public string DisplayName => "Apps";

    // Classic app installs add/remove .lnk shortcuts under the Start Menu roots,
    // which AppsFolder reflects — so a change there re-enumerates apps.
    public IReadOnlyList<string> WatchRoots { get; } = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    };

    public Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct) =>
        ShellNamespace.EnumerateAppsAsync(ct);
}
