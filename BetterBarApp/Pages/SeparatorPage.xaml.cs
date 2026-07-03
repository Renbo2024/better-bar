using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class SeparatorPage : Page
{
    private readonly SeparatorItem? _item;
    private readonly BarDefinition? _def;
    private bool _loaded;

    public SeparatorPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as SeparatorItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            MarginBox.Value = _item.Margin;
            VisibleBox.IsChecked = _item.Visible;
            GrowBox.IsChecked    = _item.GrowToFill;
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.Margin = MarginBox.Value;
        _item.Visible    = VisibleBox.IsChecked == true;
        _item.GrowToFill = GrowBox.IsChecked == true;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
