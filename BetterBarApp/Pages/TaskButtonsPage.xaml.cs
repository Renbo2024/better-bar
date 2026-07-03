using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class TaskButtonsPage : Page
{
    private readonly TaskButtonsItem? _item;
    private readonly BarDefinition?   _def;
    private bool _loaded;

    public TaskButtonsPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as TaskButtonsItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            MonitorCombo.SelectedIndex = _item.ShowAllMonitors ? 1 : 0;
            RowsBox.Value = _item.Rows;
            MaxWidthBox.Value = _item.MaxButtonWidth;
            GrowBox.IsChecked  = _item.GrowToFill;
            AccentThicknessBox.Value = _item.AccentThickness;
            AccentColorPicker.Value  = _item.AccentColor;
            SelectedPillBox.Value    = _item.SelectedPillPercent;
            UnselectedPillBox.Value  = _item.UnselectedPillPercent;
            TextMarginBox.Value      = _item.TextMargin;
            HorizontalSpacingBox.Value = _item.HorizontalSpacing;
            TooltipsBox.IsChecked      = _item.ShowTooltips;
            TooltipDelayBox.Value      = _item.TooltipDelayMs;
            UnselectedAccentColorPicker.Value = _item.UnselectedAccentColor;
            PriorityBox.Text         = _item.PriorityOrder;
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.Rows = RowsBox.Value;
        _item.MaxButtonWidth = MaxWidthBox.Value;
        _item.ShowAllMonitors = MonitorCombo.SelectedIndex == 1;
        _item.GrowToFill = GrowBox.IsChecked == true;
        _item.AccentThickness = AccentThicknessBox.Value;
        _item.AccentColor     = AccentColorPicker.Value;
        _item.SelectedPillPercent   = SelectedPillBox.Value;
        _item.UnselectedPillPercent = UnselectedPillBox.Value;
        _item.TextMargin            = TextMarginBox.Value;
        _item.HorizontalSpacing     = HorizontalSpacingBox.Value;
        _item.ShowTooltips          = TooltipsBox.IsChecked == true;
        _item.TooltipDelayMs        = TooltipDelayBox.Value;
        _item.UnselectedAccentColor = UnselectedAccentColorPicker.Value;
        _item.PriorityOrder         = PriorityBox.Text;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
