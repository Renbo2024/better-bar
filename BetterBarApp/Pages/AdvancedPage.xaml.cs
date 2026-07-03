using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class AdvancedPage : Page
{
    public AdvancedPage()
    {
        InitializeComponent();
        StartupToggle.IsChecked    = StartupService.IsEnabled;
        HideTaskbarToggle.IsChecked = AppPrefs.HideNativeTaskbar;
        InitUpdateCard();
    }

    private void InitUpdateCard()
    {
        if (!UpdateService.IsInstalled)
        {
            // Running unpackaged (e.g. `dotnet run`) — there's nothing to update.
            UpdateStatus.Text = "Updates apply only to the installed app. This copy isn't managed by the installer.";
            UpdateButton.IsEnabled = false;
            return;
        }

        if (UpdateService.UpdateReady)
        {
            // A background check already downloaded an update this session.
            ShowReady(UpdateService.ReadyVersion);
            return;
        }

        UpdateStatus.Text = $"Current version {UpdateService.CurrentVersion}. Check GitHub for a newer version.";
    }

    private void ShowReady(string? version)
    {
        UpdateStatus.Text = $"Version {version} is ready. Restart BetterBar to finish updating.";
        UpdateButton.Content = "Restart to update";
        UpdateButton.IsEnabled = true;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        // If an update is already staged (from this click or the startup check), apply + restart.
        if (UpdateService.UpdateReady)
        {
            UpdateService.ApplyAndRestart();
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking for updates…";

        var version = await UpdateService.CheckAndDownloadAsync();

        if (version != null)
        {
            ShowReady(version);
        }
        else
        {
            UpdateStatus.Text = $"You're on the latest version ({UpdateService.CurrentVersion}).";
            UpdateButton.IsEnabled = true;
        }
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e) =>
        StartupService.SetEnabled(StartupToggle.IsChecked == true);

    private void HideTaskbar_Toggled(object sender, RoutedEventArgs e)
    {
        bool hide = HideTaskbarToggle.IsChecked == true;
        AppPrefs.HideNativeTaskbar = hide;
        AppPrefs.Save();
        // Apply immediately.
        if (hide) TaskbarHider.HideAll();
        else      TaskbarHider.ShowAll();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        new ExportConfigWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = ConfigTransferService.FileFilter,
            Title = "Import configuration",
        };
        if (dlg.ShowDialog() != true) return;

        var bundle = ConfigTransferService.Read(dlg.FileName);
        if (bundle == null)
        {
            MessageBox.Show(Window.GetWindow(this)!, "That file isn't a valid BetterBar configuration.",
                "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        new ImportConfigWindow(bundle) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
