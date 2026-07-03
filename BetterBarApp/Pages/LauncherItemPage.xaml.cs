using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;
using Microsoft.Win32;

namespace BetterBarApp.Pages;

public partial class LauncherItemPage : Page
{
    private readonly LauncherItem?  _item;
    private readonly BarDefinition? _def;
    private bool _loaded;

    public LauncherItemPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as LauncherItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            DirectoryBox.Text = _item.SourceDirectory;
            RowsBox.Value = _item.Rows;
            SpacingBox.Value = _item.IconSpacing;
            MarginBox.Value = _item.IconMargin;
            TooltipsBox.IsChecked = _item.ShowTooltips;
            TooltipDelayBox.Value = _item.TooltipDelayMs;
        }
        _loaded = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select source folder" };
        if (dlg.ShowDialog() == true)
        {
            DirectoryBox.Text = dlg.FolderName;
            Changed(sender, e);
        }
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.SourceDirectory = DirectoryBox.Text.Trim();
        _item.Rows = RowsBox.Value;
        _item.IconSpacing = SpacingBox.Value;
        _item.IconMargin = MarginBox.Value;
        _item.ShowTooltips = TooltipsBox.IsChecked == true;
        _item.TooltipDelayMs = TooltipDelayBox.Value;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
