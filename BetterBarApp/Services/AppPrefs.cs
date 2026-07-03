using System.IO;
using System.Text.Json;
using BetterBarApp.Models;

namespace BetterBarApp.Services;

/// <summary>
/// App-level preferences that aren't tied to a single panel (e.g. the selected
/// theme). Kept in its own file so the panel list format in SettingsService
/// stays a plain array and round-trips unchanged.
/// </summary>
public static class AppPrefs
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterBar");

    private static readonly string FilePath = Path.Combine(Dir, "app.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    /// <summary>Name of the active theme; matches a <see cref="ThemeService.ThemeInfo.Name"/>.</summary>
    public static string Theme { get; set; } = "Dark";

    /// <summary>Hide the Explorer taskbar(s) while BetterBar runs (restored on exit). Default true.</summary>
    public static bool HideNativeTaskbar { get; set; } = true;

    // ── Legacy global search config (pre per-button search) ──────────────────────
    // Search config now lives on each StartButtonItem. These fields are still loaded
    // from app.json so the one-time migration in App startup can copy old global
    // settings onto existing start buttons; they're no longer saved back.
    public static List<SearchLocation> LegacySearchLocations { get; set; } = new();
    public static bool LegacyFrecencyApps        { get; set; }
    public static bool LegacyFrecencySettings    { get; set; }
    public static bool LegacyFrecencyQuickLaunch { get; set; }
    public static bool LegacyFrecencyDocuments   { get; set; }
    /// <summary>True if app.json still carried any global search config to migrate.</summary>
    public static bool HasLegacySearchConfig { get; private set; }

    /// <summary>Tunable search scorer weights (advanced; edit app.json to change). Shared by all engines.</summary>
    public static Search.SearchWeights SearchWeights { get; set; } = new();

    private class Model
    {
        public string Theme { get; set; } = "Dark";
        public bool HideNativeTaskbar { get; set; } = true;
        // Legacy global search fields — read for migration, written as null going forward.
        public List<SearchLocation>? SearchLocations { get; set; }
        public bool FrecencyApps        { get; set; }
        public bool FrecencySettings    { get; set; }
        public bool FrecencyQuickLaunch { get; set; }
        public bool FrecencyDocuments   { get; set; }
        public Search.SearchWeights SearchWeights { get; set; } = new();
    }

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath), Opts);
            if (model != null)
            {
                Theme = model.Theme;
                HideNativeTaskbar = model.HideNativeTaskbar;
                LegacySearchLocations    = model.SearchLocations ?? new();
                LegacyFrecencyApps        = model.FrecencyApps;
                LegacyFrecencySettings    = model.FrecencySettings;
                LegacyFrecencyQuickLaunch = model.FrecencyQuickLaunch;
                LegacyFrecencyDocuments   = model.FrecencyDocuments;
                HasLegacySearchConfig = LegacySearchLocations.Count > 0
                    || LegacyFrecencyApps || LegacyFrecencySettings
                    || LegacyFrecencyQuickLaunch || LegacyFrecencyDocuments;
                SearchWeights = model.SearchWeights ?? new();
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // Legacy global search fields are intentionally not written back — search
            // config now lives on each StartButtonItem (persisted via SettingsService).
            File.WriteAllText(FilePath, JsonSerializer.Serialize(
                new Model
                {
                    Theme             = Theme,
                    HideNativeTaskbar = HideNativeTaskbar,
                    SearchWeights     = SearchWeights,
                }, Opts));
        }
        catch { }
    }
}
