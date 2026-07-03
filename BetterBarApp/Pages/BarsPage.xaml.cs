using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

/// <summary>
/// Lists the bar definitions as cards. Add / clone / delete and open the editor.
/// (The editor is still a dialog for now; it will become an in-window page.)
/// </summary>
public partial class BarsPage : Page
{
    public BarsPage()
    {
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);
        DefinitionList.ItemsSource = PanelManager.Definitions;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var def = PanelManager.NewDefinition();
        SettingsService.Save();
        OpenEditor(def);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarDefinition def) OpenEditor(def);
    }

    private void Card_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarDefinition def) OpenEditor(def);
    }

    private void Clone_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarDefinition def)
        {
            PanelManager.CloneDefinition(def);
            SettingsService.Save();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarDefinition def)
        {
            PanelManager.RemoveDefinition(def);
            SettingsService.Save();
        }
    }

    private void OpenEditor(BarDefinition def) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), def);
}
