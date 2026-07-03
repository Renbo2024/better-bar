using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class AudioControlPage : Page
{
    private readonly AudioControlItem? _item;
    private readonly BarDefinition?    _def;
    private bool _loaded;

    public AudioControlPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as AudioControlItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            ShowSpeakerBox.IsChecked = _item.ShowSpeaker;
            ShowMicBox.IsChecked     = _item.ShowMicrophone;
            IconSizeBox.Value        = _item.IconSize;
            HMarginBox.Value         = _item.HorizontalMargin;
            IconSpacingBox.Value     = _item.IconSpacing;
            GapBox.Value             = _item.IconMeterGap;
            MeterThicknessBox.Value  = _item.MeterThickness;
            MeterColorPicker.Value   = _item.MeterColor;
            SmoothingBox.Value       = _item.MeterSmoothing;
            AutoScaleBox.IsChecked   = _item.MeterAutoScale;
            ScaleSpeedBox.Value      = _item.MeterScaleSpeed;
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.ShowSpeaker     = ShowSpeakerBox.IsChecked == true;
        _item.ShowMicrophone  = ShowMicBox.IsChecked == true;
        _item.IconSize        = IconSizeBox.Value;
        _item.HorizontalMargin = HMarginBox.Value;
        _item.IconSpacing     = IconSpacingBox.Value;
        _item.IconMeterGap    = GapBox.Value;
        _item.MeterThickness  = MeterThicknessBox.Value;
        _item.MeterColor      = MeterColorPicker.Value;
        _item.MeterSmoothing  = SmoothingBox.Value;
        _item.MeterAutoScale  = AutoScaleBox.IsChecked == true;
        _item.MeterScaleSpeed = ScaleSpeedBox.Value;
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
