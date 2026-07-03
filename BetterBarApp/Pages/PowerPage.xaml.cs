using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class PowerPage : Page
{
    private readonly PowerItem?     _item;
    private readonly BarDefinition? _def;
    private bool _loaded;

    public PowerPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as PowerItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        FontCombo.ItemsSource = Fonts.SystemFontFamilies
            .Select(f => f.Source).Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        if (_item != null)
        {
            ShowPowerBox.IsChecked     = _item.ShowPower;
            ShowRebootBox.IsChecked    = _item.ShowReboot;
            ShowHibernateBox.IsChecked = _item.ShowHibernate;
            ShowSleepBox.IsChecked     = _item.ShowSleep;
            ShowLogOffBox.IsChecked    = _item.ShowLogOff;
            ShowLabelsBox.IsChecked    = _item.ShowLabels;
            IconSizeBox.Value          = _item.IconSize;
            SpacingBox.Value           = _item.IconSpacing;
            MarginBox.Value            = _item.OuterMargin;
            FontCombo.SelectedItem     = _item.LabelFontFamily;
            FontSizeBox.Value          = _item.LabelFontSize;
            ConfirmBox.IsChecked       = _item.ConfirmAction;

            HibernateNote.Text = PowerActions.HibernateAvailable
                ? "Supported on this PC."
                : "Not available on this PC — it won't be shown.";
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.ShowPower     = ShowPowerBox.IsChecked == true;
        _item.ShowReboot    = ShowRebootBox.IsChecked == true;
        _item.ShowHibernate = ShowHibernateBox.IsChecked == true;
        _item.ShowSleep     = ShowSleepBox.IsChecked == true;
        _item.ShowLogOff    = ShowLogOffBox.IsChecked == true;
        _item.ShowLabels    = ShowLabelsBox.IsChecked == true;
        _item.IconSize      = IconSizeBox.Value;
        _item.IconSpacing   = SpacingBox.Value;
        _item.OuterMargin   = MarginBox.Value;
        if (FontCombo.SelectedItem is string font) _item.LabelFontFamily = font;
        _item.LabelFontSize = FontSizeBox.Value;
        _item.ConfirmAction = ConfirmBox.IsChecked == true;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
