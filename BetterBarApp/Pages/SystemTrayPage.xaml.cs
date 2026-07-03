using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class SystemTrayPage : Page
{
    private readonly SystemTrayItem? _item;
    private readonly BarDefinition?  _def;
    private bool _loaded;

    public SystemTrayPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as SystemTrayItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            RowsBox.Value          = _item.Rows;
            SpacingBox.Value       = _item.IconSpacing;
            MarginBox.Value        = _item.IconMargin;
            ExcludeSoundBox.IsChecked = _item.ExcludeSound;
            ExcludeMicBox.IsChecked   = _item.ExcludeMicrophone;
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.Rows         = RowsBox.Value;
        _item.IconSpacing  = SpacingBox.Value;
        _item.IconMargin   = MarginBox.Value;
        _item.ExcludeSound = ExcludeSoundBox.IsChecked == true;
        _item.ExcludeMicrophone = ExcludeMicBox.IsChecked == true;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
