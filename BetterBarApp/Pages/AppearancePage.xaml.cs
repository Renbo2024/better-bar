using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class AppearancePage : Page
{
    private bool _loaded;

    public AppearancePage()
    {
        InitializeComponent();
        ThemeSelector.ItemsSource  = ThemeService.Available.Select(t => t.Name).ToList();
        ThemeSelector.SelectedItem = ThemeService.Current.Name;
        _loaded = true;
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (ThemeSelector.SelectedItem is string name && name != ThemeService.Current.Name)
            ThemeService.Apply(name);
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(ThemeEditorPage));
}
