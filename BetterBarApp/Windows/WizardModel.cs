using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using BetterBarApp.Models;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

/// <summary>
/// Holds the choices made in the first-run setup wizard and turns them into a bar. The wizard offers a
/// deliberately small set of yes/no choices per item (details are left to Settings), then
/// <see cref="BuildDefinition"/> composes them into a <see cref="BarDefinition"/> and
/// <see cref="ApplyAsPrimaryBottom"/> installs it as the bottom bar on the primary monitor —
/// replacing whatever bottom-primary bar already exists, or creating one if none does.
///
/// The properties are observable so the wizard's step panels can two-way bind to them directly.
/// </summary>
public partial class WizardModel : ObservableObject
{
    // ── Bar ──
    [ObservableProperty] private int _heightPx = 48;

    // ── Start Button (always included) ──
    [ObservableProperty] private bool _searchApps      = true;
    [ObservableProperty] private bool _searchSettings  = true;
    [ObservableProperty] private bool _searchDocuments;

    // ── Quick Launch (optional) ──
    [ObservableProperty] private bool   _includeQuickLaunch;
    [ObservableProperty] private string _quickLaunchFolder = "";
    [ObservableProperty] private int    _quickLaunchRows   = 1;

    // ── Task Buttons (always included; grows to fill) ──
    [ObservableProperty] private bool _taskAllMonitors;
    [ObservableProperty] private int  _taskRows = 1;

    // ── Audio Control (optional) ──
    [ObservableProperty] private bool _includeAudio = true;
    [ObservableProperty] private bool _audioSpeaker = true;
    [ObservableProperty] private bool _audioMicrophone;

    // ── System Tray (always included) ──
    [ObservableProperty] private int _trayRows = 1;

    // ── Clock (optional) ──
    [ObservableProperty] private bool _includeClock = true;
    [ObservableProperty] private bool _clockShowDate = true;
    [ObservableProperty] private bool _clock24Hour;
    [ObservableProperty] private bool _clockShowSeconds;

    // ── Theme ──
    [ObservableProperty] private string _themeName = ThemeService.Current.Name;

    /// <summary>Builds the bar layout from the current choices (does not touch any live panels).</summary>
    public BarDefinition BuildDefinition()
    {
        var def = new BarDefinition { Name = "Main", HeightPx = HeightPx };

        // 1. Start button
        def.Items.Add(new StartButtonItem
        {
            SearchApps      = SearchApps,
            SearchSettings  = SearchSettings,
            SearchDocuments = SearchDocuments,
        });

        // 2. Separator + Quick Launch (optional)
        if (IncludeQuickLaunch)
        {
            def.Items.Add(new SeparatorItem());
            def.Items.Add(new LauncherItem
            {
                SourceDirectory = QuickLaunchFolder.Trim(),
                Rows            = QuickLaunchRows,
            });
        }

        // 3. Separator + Task buttons (grow to fill)
        def.Items.Add(new SeparatorItem());
        def.Items.Add(new TaskButtonsItem
        {
            GrowToFill      = true,
            ShowAllMonitors = TaskAllMonitors,
            Rows            = TaskRows,
        });

        // 4. Audio controls (optional) — sits to the right of the grow-to-fill task buttons
        if (IncludeAudio)
        {
            def.Items.Add(new AudioControlItem
            {
                ShowSpeaker    = AudioSpeaker,
                ShowMicrophone = AudioMicrophone,
            });
        }

        // 5. System tray
        def.Items.Add(new SystemTrayItem { Rows = TrayRows });

        // 6. Clock (optional)
        if (IncludeClock)
        {
            def.Items.Add(new ClockItem
            {
                ShowDate    = ClockShowDate,
                Use24Hour   = Clock24Hour,
                ShowSeconds = ClockShowSeconds,
            });
        }

        return def;
    }

    /// <summary>
    /// Installs the built bar as the bottom bar on the primary monitor. <b>Every</b> bar already placed
    /// on the bottom of the primary monitor is removed first (there can be more than one, and the AppBar
    /// API otherwise stacks them), and any definition left with no placements is dropped. A fresh
    /// bottom-primary placement is then created for the new definition. Persists.
    /// </summary>
    public void ApplyAsPrimaryBottom()
    {
        var def = BuildDefinition();

        // Remove all existing bottom-primary placements (enabled or not) so the new bar replaces rather
        // than stacks on top of them.
        var existing = PanelManager.Panels
            .Where(p => p.Position == PanelPosition.Bottom && p.MonitorNumber == 0)
            .ToList();
        var oldDefIds = existing.Select(p => p.DefinitionId).Distinct().ToList();

        foreach (var p in existing)
            PanelManager.RemovePanel(p);   // closes the live bar and drops the placement

        foreach (var id in oldDefIds)
        {
            var oldDef = PanelManager.GetDefinition(id);
            if (oldDef != null && !PanelManager.PanelsFor(oldDef).Any())
                PanelManager.RemoveDefinition(oldDef);
        }

        PanelManager.AddDefinition(def);
        var panel = new PanelConfig
        {
            DefinitionId  = def.Id,
            Position      = PanelPosition.Bottom,
            MonitorNumber = 0,
        };
        PanelManager.AddPanel(panel);
        PanelManager.EnablePanel(panel);

        SettingsService.Save();
    }
}
