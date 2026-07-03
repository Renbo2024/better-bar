using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class ClockPage : Page
{
    private readonly ClockItem?     _item;
    private readonly BarDefinition? _def;
    private readonly ObservableCollection<LineRow> _order = new();
    private ICollectionView? _zoneView;
    private bool _loaded;

    public ClockPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as ClockItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        // Font pickers — installed font families.
        var fonts = Fonts.SystemFontFamilies
            .Select(f => f.Source).Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        TimeFontCombo.ItemsSource  = fonts;
        DateFontCombo.ItemsSource  = fonts;
        ZoneFontCombo.ItemsSource  = fonts;
        TitleFontCombo.ItemsSource = fonts;

        // Searchable time-zone picker — "My Computer's Time Zone" then every system zone.
        var zones = new List<ZoneOption> { new("", "My Computer's Time Zone") };
        zones.AddRange(TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(z => z.BaseUtcOffset).ThenBy(z => z.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(z => new ZoneOption(z.Id, z.DisplayName)));
        ZoneCombo.ItemsSource = zones;
        _zoneView = CollectionViewSource.GetDefaultView(zones);
        ZoneCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ZoneText_Changed));
        ZoneCombo.DropDownClosed += (_, _) => ClearZoneFilter();

        if (_item != null)
        {
            MarginBox.Value = _item.Margin;
            ShowTimeBox.IsChecked  = _item.ShowTime;
            ShowDateBox.IsChecked  = _item.ShowDate;
            ShowZoneBox.IsChecked  = _item.ShowTimeZone;
            ShowTitleBox.IsChecked = _item.ShowTitle;

            Use24Box.IsChecked    = _item.Use24Hour;
            SecondsBox.IsChecked  = _item.ShowSeconds;
            AmPmBox.IsChecked     = _item.ShowAmPm;
            HourZeroBox.IsChecked = _item.HourLeadingZero;
            TimeFontCombo.SelectedItem = _item.TimeFontFamily;
            TimeSizeBox.Value = _item.TimeFontSize;
            TimeBoldBox.IsChecked = _item.TimeBold;
            TimeItalicBox.IsChecked = _item.TimeItalic;
            TimeColorPicker.Value = _item.TimeColor;

            WeekdayCombo.SelectedIndex = (int)_item.Weekday;
            MonthCombo.SelectedIndex   = (int)_item.MonthStyle;
            DayZeroBox.IsChecked       = _item.DayLeadingZero;
            YearCombo.SelectedIndex    = (int)_item.Year;
            DayBeforeMonthBox.IsChecked = _item.DayBeforeMonth;
            DateFontCombo.SelectedItem = _item.DateFontFamily;
            DateSizeBox.Value = _item.DateFontSize;
            DateBoldBox.IsChecked = _item.DateBold;
            DateItalicBox.IsChecked = _item.DateItalic;
            DateColorPicker.Value = _item.DateColor;

            ZoneCombo.SelectedItem = zones.FirstOrDefault(z => z.Id == _item.TimeZoneId) ?? zones[0];
            ZoneFontCombo.SelectedItem = _item.TimeZoneFontFamily;
            ZoneSizeBox.Value = _item.TimeZoneFontSize;
            ZoneBoldBox.IsChecked = _item.TimeZoneBold;
            ZoneItalicBox.IsChecked = _item.TimeZoneItalic;
            ZoneColorPicker.Value = _item.TimeZoneColor;

            TitleBox.Text = _item.Title;
            TitleFontCombo.SelectedItem = _item.TitleFontFamily;
            TitleSizeBox.Value = _item.TitleFontSize;
            TitleBoldBox.IsChecked = _item.TitleBold;
            TitleItalicBox.IsChecked = _item.TitleItalic;
            TitleColorPicker.Value = _item.TitleColor;

            foreach (var line in NormalizeOrder(_item.LineOrder))
                _order.Add(new LineRow(line, LineName(line)));
            OrderList.ItemsSource = _order;
        }
        _loaded = true;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;

        _item.Margin = MarginBox.Value;
        _item.ShowTime     = ShowTimeBox.IsChecked == true;
        _item.ShowDate     = ShowDateBox.IsChecked == true;
        _item.ShowTimeZone = ShowZoneBox.IsChecked == true;
        _item.ShowTitle    = ShowTitleBox.IsChecked == true;

        _item.Use24Hour       = Use24Box.IsChecked == true;
        _item.ShowSeconds     = SecondsBox.IsChecked == true;
        _item.ShowAmPm        = AmPmBox.IsChecked == true;
        _item.HourLeadingZero = HourZeroBox.IsChecked == true;
        if (TimeFontCombo.SelectedItem is string tf) _item.TimeFontFamily = tf;
        _item.TimeFontSize = TimeSizeBox.Value;
        _item.TimeBold   = TimeBoldBox.IsChecked == true;
        _item.TimeItalic = TimeItalicBox.IsChecked == true;
        _item.TimeColor  = TimeColorPicker.Value;

        _item.Weekday        = (ClockWeekday)Math.Max(0, WeekdayCombo.SelectedIndex);
        _item.MonthStyle     = (ClockMonthStyle)Math.Max(0, MonthCombo.SelectedIndex);
        _item.DayLeadingZero = DayZeroBox.IsChecked == true;
        _item.Year           = (ClockYearStyle)Math.Max(0, YearCombo.SelectedIndex);
        _item.DayBeforeMonth = DayBeforeMonthBox.IsChecked == true;
        if (DateFontCombo.SelectedItem is string df) _item.DateFontFamily = df;
        _item.DateFontSize = DateSizeBox.Value;
        _item.DateBold   = DateBoldBox.IsChecked == true;
        _item.DateItalic = DateItalicBox.IsChecked == true;
        _item.DateColor  = DateColorPicker.Value;

        if (ZoneCombo.SelectedItem is ZoneOption zo) _item.TimeZoneId = zo.Id;
        if (ZoneFontCombo.SelectedItem is string zf) _item.TimeZoneFontFamily = zf;
        _item.TimeZoneFontSize = ZoneSizeBox.Value;
        _item.TimeZoneBold   = ZoneBoldBox.IsChecked == true;
        _item.TimeZoneItalic = ZoneItalicBox.IsChecked == true;
        _item.TimeZoneColor  = ZoneColorPicker.Value;

        _item.Title = TitleBox.Text;
        if (TitleFontCombo.SelectedItem is string ttf) _item.TitleFontFamily = ttf;
        _item.TitleFontSize = TitleSizeBox.Value;
        _item.TitleBold   = TitleBoldBox.IsChecked == true;
        _item.TitleItalic = TitleItalicBox.IsChecked == true;
        _item.TitleColor  = TitleColorPicker.Value;

        Apply();
    }

    // ── Time-zone search filtering ──
    private void ZoneText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_zoneView == null) return;
        var t = ZoneCombo.Text;
        // Skip when the text is just the selected item's label (a selection set it).
        if (ZoneCombo.SelectedItem is ZoneOption sel && sel.Label == t) return;
        _zoneView.Filter = t.Length == 0
            ? null
            : o => ((ZoneOption)o).Label.Contains(t, StringComparison.OrdinalIgnoreCase);
        _zoneView.Refresh();
        if (t.Length > 0) ZoneCombo.IsDropDownOpen = true;
    }

    private void ClearZoneFilter()
    {
        if (_zoneView?.Filter == null) return;
        _zoneView.Filter = null;
        _zoneView.Refresh();
    }

    // ── Line order ──
    private void OrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = OrderList.SelectedIndex;
        OrderUpBtn.IsEnabled   = i > 0;
        OrderDownBtn.IsEnabled = i >= 0 && i < _order.Count - 1;
    }

    private void OrderUp_Click(object sender, RoutedEventArgs e)   => MoveOrder(-1);
    private void OrderDown_Click(object sender, RoutedEventArgs e) => MoveOrder(+1);

    private void MoveOrder(int dir)
    {
        int i = OrderList.SelectedIndex;
        int j = i + dir;
        if (i < 0 || j < 0 || j >= _order.Count) return;
        _order.Move(i, j);
        OrderList.SelectedIndex = j;
        if (_item != null) { _item.LineOrder = _order.Select(r => r.Line).ToList(); Apply(); }
    }

    private void Apply()
    {
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);

    private static string LineName(ClockLine l) => l switch
    {
        ClockLine.Time => "Time", ClockLine.Date => "Date",
        ClockLine.TimeZone => "Time zone", _ => "Title",
    };

    // Ensure every line appears exactly once, keeping the saved order.
    private static List<ClockLine> NormalizeOrder(List<ClockLine>? src)
    {
        var all = new[] { ClockLine.Title, ClockLine.Time, ClockLine.Date, ClockLine.TimeZone };
        var result = new List<ClockLine>();
        foreach (var l in src ?? new()) if (all.Contains(l) && !result.Contains(l)) result.Add(l);
        foreach (var l in all) if (!result.Contains(l)) result.Add(l);
        return result;
    }

    private sealed record ZoneOption(string Id, string Label) { public override string ToString() => Label; }
    private sealed record LineRow(ClockLine Line, string Name)  { public override string ToString() => Name; }
}
