using System.Linq;
using System.Windows;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Services.Search;
using BetterBarApp.Windows;

namespace BetterBarApp
{
    public partial class App : Application
    {
        /// <summary>Set from <see cref="Program.Main"/> when launched with <c>--setup</c>: run the setup
        /// wizard even if a configuration already exists.</summary>
        public static bool ForceSetupWizard { get; set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Load prefs before anything that depends on them (e.g. whether to hide the taskbar).
            AppPrefs.Load();
            // Hide Windows taskbars BEFORE loading panels so each panel's AppBar registration sees the
            // screen edge as free space and takes it cleanly — unless the user opted out.
            if (AppPrefs.HideNativeTaskbar) TaskbarHider.HideAll();
            // Number the monitors (top→bottom, then left→right; primary excluded)
            // before panels load so MonitorDisplay resolves to "Screen N".
            ScreenService.Detect();
            // Apply the saved theme before panels are built so they're born with
            // the right palette (DynamicResource would update them live anyway).
            ThemeService.Initialize();
            // Put WPF-UI into Dark so the FluentWindow settings app gets the dark Mica
            // chrome and themed controls. This is independent of the taskbar palette.
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                Wpf.Ui.Appearance.ApplicationTheme.Dark, updateAccent: false);
            SettingsService.Load();
            // First run (no saved bar definitions) — or an explicit "--setup" launch — runs the setup
            // wizard, which builds a bar and installs it on the bottom of the primary monitor. Done before
            // the search-warm / Win-key / empty-profile steps below so they all see the resulting bar.
            bool firstRun = PanelManager.Definitions.Count == 0;
            if (firstRun || ForceSetupWizard)
            {
                bool finished = SetupWizardWindow.RunModal();
                // If the user cancelled a true first-run, build the default bar (the wizard's own defaults)
                // so there's always a bar. (Cancelling a forced --setup on a configured system changes nothing.)
                if (!finished && firstRun)
                    new WizardModel().ApplyAsPrimaryBottom();
            }
            // Cheap create-only poll: bring up bars for monitors that connect after launch (RDP). It
            // never closes a live bar, so it can't disturb existing reservations.
            PanelManager.StartScreenPolling();
            // Search config is now per start button; migrate any old global config and
            // start each button's private engine warming in the background (spec §10.1).
            MigrateAndWarmStartButtons();

            bool hasStartButton = PanelManager.Definitions
                .SelectMany(d => d.Items)
                .OfType<StartButtonItem>()
                .Any();

            // Take over the Windows key to open our Start Button (leftmost on the highest-priority
            // monitor — see PanelManager.FindPrimaryStartButton) when one exists.
            if (hasStartButton)
                WinKeyHook.Install(() => PanelManager.FindPrimaryStartButton()?.ToggleMenu());

            // The config screen no longer opens on startup. With nothing else configured there'd be
            // no way in, so show it only on a truly empty (first-run) profile.
            if (PanelManager.Definitions.Count == 0)
                SettingsWindow.ShowOrActivate();

            // Check GitHub for a newer release in the background and stage it for the next restart.
            // No-ops cleanly when not running as an installed Velopack app (e.g. `dotnet run`). The
            // staged update surfaces non-intrusively on the Advanced settings page ("restart to apply").
            _ = UpdateService.CheckAndDownloadAsync();
        }

        // One-time upgrade: copy the old global search folders / recency flags onto every
        // existing start button, then warm each button's own engine.
        private static void MigrateAndWarmStartButtons()
        {
            var buttons = PanelManager.Definitions
                .SelectMany(d => d.Items)
                .OfType<StartButtonItem>()
                .ToList();

            if (AppPrefs.HasLegacySearchConfig)
            {
                foreach (var b in buttons)
                {
                    if (b.SearchLocations.Count == 0)
                        b.SearchLocations = AppPrefs.LegacySearchLocations
                            .Select(l => new SearchLocation
                            {
                                Name = l.Name, Path = l.Path, Cascade = l.Cascade,
                                Frecency = l.Frecency, IncludeRegex = l.IncludeRegex, ExcludeRegex = l.ExcludeRegex,
                            }).ToList();
                    b.FrecencyApps        = AppPrefs.LegacyFrecencyApps;
                    b.FrecencySettings    = AppPrefs.LegacyFrecencySettings;
                    b.FrecencyQuickLaunch = AppPrefs.LegacyFrecencyQuickLaunch;
                    b.FrecencyDocuments   = AppPrefs.LegacyFrecencyDocuments;
                }
                SettingsService.Save();   // persist migrated config onto the items
                AppPrefs.Save();          // rewrites app.json without the legacy fields
            }

            foreach (var b in buttons) StartSearch.EnsureBuilt(b);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { WinKeyHook.Uninstall(); } catch { }
            try { TrayHostService.Shutdown(); } catch { }
            // Restore the Explorer taskbar first — if Save() ever throws,
            // we still want the user's normal taskbar back.
            try { TaskbarHider.ShowAll(); } catch { }
            SettingsService.Save();
            base.OnExit(e);
        }
    }
}
