using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Services;
using Microsoft.Win32;

namespace BetterBarApp.Windows;

/// <summary>
/// First-run setup wizard: a small stepped dialog that asks a handful of yes/no questions and then
/// installs a bar along the bottom of the primary monitor (see <see cref="WizardModel"/>). Shown on a
/// first run with no bars, or on demand via the <c>--setup</c> command-line flag. Runs modally during
/// startup, before the rest of the boot sequence continues.
/// </summary>
public partial class SetupWizardWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly WizardModel _model = new();

    // The active theme when the wizard opened, so Cancel can undo a live theme preview.
    private readonly string _originalTheme = ThemeService.Current.Name;

    // Ordered steps: the panel shown and its header title. Order matches the on-bar item order.
    private readonly List<(ScrollViewer Panel, string Title)> _steps;
    private int _index;

    /// <summary>Shows the wizard modally. Returns true if the user finished (and the bar was applied).</summary>
    public static bool RunModal() => new SetupWizardWindow().ShowDialog() == true;

    public SetupWizardWindow()
    {
        InitializeComponent();
        DataContext = _model;

        _steps =
        [
            (StepWelcome,     "Welcome"),
            (StepStart,       "Start Button"),
            (StepQuickLaunch, "Quick Launch"),
            (StepTasks,       "Task Buttons"),
            (StepAudio,       "Audio Control"),
            (StepTray,        "System Tray"),
            (StepClock,       "Clock"),
            (StepTheme,       "Theme"),
            (StepSummary,     "All set"),
        ];

        StepProgress.Maximum = _steps.Count - 1;

        ThemeList.ItemsSource   = ThemeService.Available.Select(t => t.Name).ToList();
        ThemeList.SelectedItem  = _model.ThemeName;

        // Re-validate the Quick Launch step whenever its inclusion or folder changes so Next can't be
        // clicked with the item enabled but no folder chosen.
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WizardModel.IncludeQuickLaunch) or nameof(WizardModel.QuickLaunchFolder))
                RefreshQuickLaunchValidation();
        };

        ShowStep(0);
    }

    // ── Navigation ────────────────────────────────────────────────────────────────

    private void ShowStep(int index)
    {
        _index = index;
        for (int i = 0; i < _steps.Count; i++)
            _steps[i].Panel.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        StepTitle.Text      = _steps[index].Title;
        StepCounter.Text     = $"Step {index + 1} of {_steps.Count}";
        StepProgress.Value   = index;
        BackButton.IsEnabled = index > 0;

        bool last = index == _steps.Count - 1;
        NextButton.Content = last ? "Finish" : "Next";
        if (last) UpdateSummary();

        RefreshQuickLaunchValidation();
    }

    /// <summary>
    /// On the Quick Launch step, requires a folder before Next when the item is included: disables Next
    /// and shows the inline warning. A no-op (Next enabled) on every other step.
    /// </summary>
    private void RefreshQuickLaunchValidation()
    {
        bool onQuickLaunch = _steps[_index].Panel == StepQuickLaunch;
        bool needsFolder   = onQuickLaunch && _model.IncludeQuickLaunch
                             && string.IsNullOrWhiteSpace(_model.QuickLaunchFolder);

        QuickLaunchWarning.Visibility = needsFolder ? Visibility.Visible : Visibility.Collapsed;
        NextButton.IsEnabled          = !needsFolder;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) ShowStep(_index - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _steps.Count - 1) { ShowStep(_index + 1); return; }

        // Finish: install the bar and close.
        _model.ApplyAsPrimaryBottom();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Undo any live theme preview so cancelling changes nothing.
        if (ThemeService.Current.Name != _originalTheme)
            ThemeService.Apply(_originalTheme);
        DialogResult = false;
    }

    // ── Per-step handlers ──────────────────────────────────────────────────────────

    private void BrowseQuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the Quick Launch shortcuts folder" };
        if (dlg.ShowDialog() == true)
            _model.QuickLaunchFolder = dlg.FolderName;
    }

    private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeList.SelectedItem is not string name) return;
        _model.ThemeName = name;
        ThemeService.Apply(name);   // live preview
    }

    private void UpdateSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"• Bar height: {_model.HeightPx}px, placed on the bottom of the primary monitor.");
        sb.AppendLine("• Start Button with search.");
        if (_model.IncludeQuickLaunch)
        {
            var folder = string.IsNullOrWhiteSpace(_model.QuickLaunchFolder) ? "(no folder yet)" : _model.QuickLaunchFolder;
            sb.AppendLine($"• Quick Launch — {folder}.");
        }
        sb.AppendLine($"• Task Buttons — {(_model.TaskAllMonitors ? "all monitors" : "this monitor")}.");
        if (_model.IncludeAudio)
        {
            var which = new List<string>();
            if (_model.AudioSpeaker)    which.Add("speaker");
            if (_model.AudioMicrophone) which.Add("microphone");
            sb.AppendLine($"• Audio Control — {(which.Count == 0 ? "no icons selected" : string.Join(" + ", which))}.");
        }
        sb.AppendLine("• System Tray.");
        if (_model.IncludeClock)
            sb.AppendLine($"• Clock — {(_model.Clock24Hour ? "24-hour" : "12-hour")}{(_model.ClockShowDate ? ", with date" : "")}.");
        sb.Append($"• Theme: {_model.ThemeName}.");
        SummaryText.Text = sb.ToString();
    }
}
