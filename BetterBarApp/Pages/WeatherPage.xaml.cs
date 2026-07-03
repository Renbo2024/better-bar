using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class WeatherPage : Page
{
    private readonly WeatherItem?   _item;
    private readonly BarDefinition? _def;
    private bool _loaded;

    public WeatherPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as WeatherItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        var fonts = Fonts.SystemFontFamilies
            .Select(f => f.Source).Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        TitleFontCombo.ItemsSource   = fonts;
        MeasureFontCombo.ItemsSource = fonts;
        LabelFontCombo.ItemsSource   = fonts;

        if (_item != null)
        {
            ShowTitleBox.IsChecked          = _item.ShowTitle;
            TitleBox.Text                   = _item.Title;
            UnitsCombo.SelectedIndex        = (int)_item.Units;

            ShowForecastBox.IsChecked       = _item.ShowForecast;
            ForecastKindCombo.SelectedIndex = (int)_item.ForecastKind;
            ForecastAmountBox.Maximum       = _item.ForecastKind == WeatherForecastKind.Days ? 7 : WeatherItem.MaxHours;
            ForecastAmountBox.Value         = _item.ForecastAmount;

            ShowTooltipBox.IsChecked = _item.ShowTooltip;
            TooltipDelayBox.Value    = _item.TooltipDelayMs;

            TitleFontCombo.SelectedItem   = _item.TitleFontFamily;
            TitleSizeBox.Value            = _item.TitleFontSize;
            MeasureFontCombo.SelectedItem = _item.MeasurementFontFamily;
            MeasureSizeBox.Value          = _item.MeasurementFontSize;
            LabelFontCombo.SelectedItem   = _item.LabelFontFamily;
            LabelSizeBox.Value            = _item.LabelFontSize;

            IconSizeBox.Value       = _item.IconSize;
            SectionSpacingBox.Value = _item.SectionSpacing;
            MarginBox.Value         = _item.Margin;
            TextColorPicker.Value   = _item.TextColor;
            RefreshBox.Value        = _item.RefreshMinutes;

            UpdateLocationText();
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;

        _item.ShowTitle    = ShowTitleBox.IsChecked == true;
        _item.Title        = TitleBox.Text;
        _item.Units        = (WeatherUnits)Math.Max(0, UnitsCombo.SelectedIndex);
        _item.ShowForecast = ShowForecastBox.IsChecked == true;
        _item.ForecastKind = (WeatherForecastKind)Math.Max(0, ForecastKindCombo.SelectedIndex);

        // The look-ahead amount caps differently for hours vs. days.
        int maxAmt = _item.ForecastKind == WeatherForecastKind.Days ? 7 : WeatherItem.MaxHours;
        if (ForecastAmountBox.Maximum != maxAmt) ForecastAmountBox.Maximum = maxAmt;
        if (ForecastAmountBox.Value > maxAmt)    ForecastAmountBox.Value   = maxAmt;
        _item.ForecastAmount = ForecastAmountBox.Value;

        _item.ShowTooltip    = ShowTooltipBox.IsChecked == true;
        _item.TooltipDelayMs = TooltipDelayBox.Value;

        if (TitleFontCombo.SelectedItem   is string tf) _item.TitleFontFamily       = tf;
        _item.TitleFontSize = TitleSizeBox.Value;
        if (MeasureFontCombo.SelectedItem is string mf) _item.MeasurementFontFamily = mf;
        _item.MeasurementFontSize = MeasureSizeBox.Value;
        if (LabelFontCombo.SelectedItem   is string lf) _item.LabelFontFamily       = lf;
        _item.LabelFontSize = LabelSizeBox.Value;

        _item.IconSize       = IconSizeBox.Value;
        _item.SectionSpacing = SectionSpacingBox.Value;
        _item.Margin         = MarginBox.Value;
        _item.TextColor      = TextColorPicker.Value;
        _item.RefreshMinutes = RefreshBox.Value;

        Apply();
    }

    // ── Location pop-up ──────────────────────────────────────────────────────────
    private void ChangeLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;
        var dlg = new LocationSearchDialog(Window.GetWindow(this), _item.LocationName);
        if (dlg.ShowDialog() != true || dlg.Result is not { } g) return;

        _item.LocationName = g.Display;
        _item.Latitude     = g.Latitude;
        _item.Longitude    = g.Longitude;
        _item.HasLocation  = true;
        UpdateLocationText();
        Apply();
    }

    private void UpdateLocationText()
    {
        if (_item == null) return;
        CurrentLocationText.Text = _item.HasLocation
            ? $"{_item.LocationName}  ({_item.Latitude.ToString("0.###", CultureInfo.InvariantCulture)}, " +
              $"{_item.Longitude.ToString("0.###", CultureInfo.InvariantCulture)})"
            : "No location selected";
    }

    private void Apply()
    {
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
