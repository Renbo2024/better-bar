using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace BetterBarApp.Services;

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> against this app's GitHub Releases feed so the
/// installed app can update itself. Velopack only does real work when the app was actually installed
/// (the <c>Setup.exe</c> path); during development (<c>dotnet run</c>) <see cref="UpdateManager.IsInstalled"/>
/// is false and every method here no-ops, so it is always safe to call.
///
/// Release flow that feeds this: <c>./pack.ps1 -Version x.y.z</c> builds the packages, then
/// <c>vpk upload github</c> publishes them as a GitHub Release. This manager reads that same repo's
/// releases and offers the newest one. See RELEASING.md.
/// </summary>
public static class UpdateService
{
    // The public GitHub repository whose Releases hold BetterBar's update packages.
    private const string RepoUrl = "https://github.com/Renbo2024/better-bar";

    private static UpdateManager? _manager;
    private static UpdateInfo? _pending;   // set once an update has been downloaded and is ready to apply

    /// <summary>True when running as an installed Velopack app (so updating is possible).</summary>
    public static bool IsInstalled => Manager?.IsInstalled == true;

    /// <summary>The currently running version, or null when not running as an installed app.</summary>
    public static string? CurrentVersion => Manager?.CurrentVersion?.ToString();

    /// <summary>True once <see cref="CheckAndDownloadAsync"/> has staged an update awaiting a restart.</summary>
    public static bool UpdateReady => _pending != null;

    /// <summary>The version that will be applied on the next restart, if an update is staged.</summary>
    public static string? ReadyVersion => _pending?.TargetFullRelease?.Version?.ToString();

    private static UpdateManager? Manager
    {
        get
        {
            if (_manager != null) return _manager;
            try
            {
                _manager = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));
            }
            catch
            {
                // Constructing the locator can throw in odd environments; treat as "not updatable".
                _manager = null;
            }
            return _manager;
        }
    }

    /// <summary>
    /// Checks GitHub for a newer release and, if found, downloads it and stages it for the next
    /// restart (sets <see cref="UpdateReady"/>). Does <b>not</b> restart the app. Returns the new
    /// version string if one was downloaded, otherwise null. Never throws.
    /// </summary>
    public static async Task<string?> CheckAndDownloadAsync()
    {
        var mgr = Manager;
        if (mgr is not { IsInstalled: true }) return null;

        try
        {
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info == null) return null;

            await mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info;
            return info.TargetFullRelease?.Version?.ToString();
        }
        catch
        {
            // Offline, rate-limited, no releases yet, etc. — silently leave the app as-is.
            return null;
        }
    }

    /// <summary>
    /// Applies a previously-downloaded update and restarts the app. No-op unless
    /// <see cref="UpdateReady"/> is true. Does not return on success (the process is replaced).
    /// </summary>
    public static void ApplyAndRestart()
    {
        var mgr = Manager;
        if (mgr == null || _pending?.TargetFullRelease == null) return;
        mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
