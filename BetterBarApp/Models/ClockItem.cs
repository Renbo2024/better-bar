using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

/// <summary>How the day-of-week is shown on the clock's date line.</summary>
public enum ClockWeekday { None, Short, Full }

/// <summary>How the month is shown: numeric (with/without leading zero) or by name.</summary>
public enum ClockMonthStyle { Number, NumberPadded, ShortName, FullName }

/// <summary>How (and whether) the year is shown.</summary>
public enum ClockYearStyle { None, TwoDigit, FourDigit }

/// <summary>The stackable lines of a clock, in their configurable top-to-bottom order.</summary>
public enum ClockLine { Time, Date, TimeZone, Title }

/// <summary>
/// A clock bar item. Optionally shows a time line and/or a date line, each with its own
/// format and font (family, size, bold, italic). The control updates every second.
/// (A future iteration will reveal a calendar on click.)
/// </summary>
public partial class ClockItem : BarItem
{
    public ClockItem()
    {
        TypeKey     = ItemTypes.Clock;
        DisplayName = "Clock";
    }

    // Horizontal space (px) on each side of the clock text — separates it from
    // neighbouring items and the bar edge.
    [ObservableProperty] private int _margin = 10;

    // ── What to show ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showTime = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _showDate = true;
    [ObservableProperty] private bool _showTimeZone;
    [ObservableProperty] private bool _showTitle;

    // ── Title (free text) ──
    [ObservableProperty] private string _title = "";

    // ── Time zone ──
    // Empty = "My Computer's Time Zone" (local); otherwise a TimeZoneInfo.Id whose time
    // and date the clock shows instead of local.
    [ObservableProperty] private string _timeZoneId = "";

    // Top-to-bottom order of the lines. Only the enabled lines render; the rest are
    // skipped while keeping this order.
    public List<ClockLine> LineOrder { get; set; } =
        new() { ClockLine.Title, ClockLine.Time, ClockLine.Date, ClockLine.TimeZone };

    // ── Time format ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private bool _use24Hour;
    [ObservableProperty] private bool _showSeconds;
    [ObservableProperty] private bool _showAmPm = true;     // only applies in 12-hour mode
    [ObservableProperty] private bool _hourLeadingZero;

    // ── Date format ──
    [ObservableProperty] private ClockWeekday    _weekday    = ClockWeekday.Short;
    [ObservableProperty] private ClockMonthStyle _monthStyle = ClockMonthStyle.ShortName;
    [ObservableProperty] private bool            _dayLeadingZero;
    [ObservableProperty] private ClockYearStyle  _year       = ClockYearStyle.FourDigit;
    [ObservableProperty] private bool            _dayBeforeMonth;

    // ── Time font ──
    [ObservableProperty] private string _timeFontFamily = "Segoe UI";
    [ObservableProperty] private int    _timeFontSize   = 14;
    [ObservableProperty] private bool   _timeBold;
    [ObservableProperty] private bool   _timeItalic;

    // ── Date font ──
    [ObservableProperty] private string _dateFontFamily = "Segoe UI";
    [ObservableProperty] private int    _dateFontSize   = 11;
    [ObservableProperty] private bool   _dateBold;
    [ObservableProperty] private bool   _dateItalic;

    // ── Time-zone-name font ──
    [ObservableProperty] private string _timeZoneFontFamily = "Segoe UI";
    [ObservableProperty] private int    _timeZoneFontSize   = 10;
    [ObservableProperty] private bool   _timeZoneBold;
    [ObservableProperty] private bool   _timeZoneItalic;

    // ── Title font ──
    [ObservableProperty] private string _titleFontFamily = "Segoe UI";
    [ObservableProperty] private int    _titleFontSize   = 12;
    [ObservableProperty] private bool   _titleBold;
    [ObservableProperty] private bool   _titleItalic;

    // ── Per-line foreground colour ("" = default theme colour; else a colour string). ──
    [ObservableProperty] private string _timeColor     = "";
    [ObservableProperty] private string _dateColor     = "";
    [ObservableProperty] private string _timeZoneColor = "";
    [ObservableProperty] private string _titleColor    = "";

    /// <summary>The effective time zone (local when no override is set).</summary>
    public TimeZoneInfo ResolveZone()
    {
        if (string.IsNullOrEmpty(TimeZoneId)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch { return TimeZoneInfo.Local; }
    }

    /// <summary>Name shown on the time-zone line.</summary>
    public string TimeZoneLabel() => ResolveZone().Id;

    /// <summary>.NET custom format string for the time line (e.g. "h:mm tt").</summary>
    public string TimeFormat()
    {
        string hour = Use24Hour ? (HourLeadingZero ? "HH" : "H")
                                 : (HourLeadingZero ? "hh" : "h");
        string fmt = hour + ":mm";
        if (ShowSeconds) fmt += ":ss";
        if (!Use24Hour && ShowAmPm) fmt += " tt";
        return fmt;
    }

    /// <summary>.NET custom format string for the date line (e.g. "ddd, MMM d, yyyy").</summary>
    public string DateFormat()
    {
        string weekday = Weekday switch
        {
            ClockWeekday.Short => "ddd",
            ClockWeekday.Full  => "dddd",
            _                  => "",
        };
        string month = MonthStyle switch
        {
            ClockMonthStyle.Number       => "M",
            ClockMonthStyle.NumberPadded => "MM",
            ClockMonthStyle.ShortName    => "MMM",
            _                            => "MMMM",
        };
        string day  = DayLeadingZero ? "dd" : "d";
        string year = Year switch
        {
            ClockYearStyle.TwoDigit  => "yy",
            ClockYearStyle.FourDigit => "yyyy",
            _                        => "",
        };
        bool monthIsName = MonthStyle is ClockMonthStyle.ShortName or ClockMonthStyle.FullName;

        string core;
        if (monthIsName)
        {
            core = DayBeforeMonth ? $"{day} {month}" : $"{month} {day}";
            if (year.Length > 0)
                core += DayBeforeMonth ? $" {year}" : $", {year}";   // "5 January 2026" / "January 5, 2026"
        }
        else
        {
            var parts = DayBeforeMonth ? new List<string> { day, month } : new List<string> { month, day };
            if (year.Length > 0) parts.Add(year);
            core = string.Join("/", parts);
        }

        return weekday.Length > 0 ? $"{weekday}, {core}" : core;
    }

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var bits = new List<string>();
            if (ShowTime) bits.Add(Use24Hour ? "24-hour time" : "12-hour time");
            if (ShowDate) bits.Add("date");
            return bits.Count > 0 ? string.Join("  ·  ", bits) : "(nothing shown)";
        }
    }
}
