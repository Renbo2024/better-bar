using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders the Audio Control item: a Speaker and/or Microphone icon button, each with a level meter
/// beneath it. Clicking an icon opens its flyout (device picker + level slider). A timer polls the
/// endpoints' peak meters; the icon glyph reflects level / mute and updates on endpoint changes.
/// </summary>
public partial class AudioControlControl : UserControl
{
    private sealed class Channel
    {
        public required AudioEndpoint Endpoint;
        public required bool          IsSpeaker;
        public required AudioMeter    Meter;
        public required SymbolIcon    Glyph;
        public required Button        IconButton;
        public Action? ChangedHandler;
    }

    private readonly AudioControlItem _item;
    private readonly DispatcherTimer  _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<Channel>    _channels = [];

    public AudioControlControl(AudioControlItem item)
    {
        _item = item;
        InitializeComponent();
        AudioService.Ensure();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Build();
        // Each visible meter opens its endpoint's metering (the mic capture stream only runs while a
        // mic meter is actually shown). Balanced in OnUnloaded.
        foreach (var ch in _channels) ch.Endpoint.AcquireMeter();
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        foreach (var ch in _channels)
        {
            ch.Endpoint.ReleaseMeter();
            if (ch.ChangedHandler != null) ch.Endpoint.Changed -= ch.ChangedHandler;
        }
        _channels.Clear();
    }

    private void Build()
    {
        Outer.Margin = new Thickness(_item.HorizontalMargin, 0, _item.HorizontalMargin, 0);
        Host.Children.Clear();
        _channels.Clear();

        Color meterColor = ParseColor(_item.MeterColor)
                           ?? (TryFindResource("Accent") as SolidColorBrush)?.Color
                           ?? Colors.DodgerBlue;

        bool first = true;
        if (_item.ShowSpeaker)    AddChannel(AudioService.Speaker,    true,  meterColor, ref first);
        if (_item.ShowMicrophone) AddChannel(AudioService.Microphone, false, meterColor, ref first);
    }

    private void AddChannel(AudioEndpoint endpoint, bool isSpeaker, Color meterColor, ref bool first)
    {
        var glyph = new SymbolIcon { FontSize = Math.Max(8, _item.IconSize) };

        var iconButton = new Button
        {
            Content    = glyph,
            Padding    = new Thickness(2, 0, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (TryFindResource("BarIconButton") is Style style) iconButton.Style = style;
        iconButton.SetResourceReference(ForegroundProperty, "TaskBtnFg");
        iconButton.Click += (_, _) => AudioFlyoutWindow.ShowFor(endpoint, isSpeaker, iconButton);

        var meter = new AudioMeter
        {
            Height = Math.Max(1, _item.MeterThickness),
            Margin = new Thickness(0, _item.IconMeterGap, 0, 0),
            // Stretch to the column width (= the icon button's width, "100%") via layout. A binding to
            // the button's ActualWidth proved fragile and could leave the meter zero-width.
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        meter.Configure(meterColor, _item.MeterThickness,
                        _item.MeterSmoothing / 100.0, _item.MeterAutoScale, _item.MeterScaleSpeed / 100.0);

        // The column's width is set by the (centred) icon button; the meter stretches to match it.
        var column = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        if (!first) column.Margin = new Thickness(_item.IconSpacing, 0, 0, 0);
        first = false;
        column.Children.Add(iconButton);
        column.Children.Add(meter);
        Host.Children.Add(column);

        var ch = new Channel { Endpoint = endpoint, IsSpeaker = isSpeaker, Meter = meter, Glyph = glyph, IconButton = iconButton };
        // Endpoint changes can arrive off the UI thread — marshal before touching WPF.
        ch.ChangedHandler = () => Dispatcher.BeginInvoke(() => UpdateGlyph(ch));
        endpoint.Changed += ch.ChangedHandler;
        _channels.Add(ch);

        UpdateGlyph(ch);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Metering must never throw out of the timer — a transient COM hiccup shouldn't kill the bar.
        try
        {
            foreach (var ch in _channels)
            {
                var (l, r) = ch.Endpoint.Peaks;
                ch.Meter.SetLevels(l, r);
            }
        }
        catch { }
    }

    private static void UpdateGlyph(Channel ch)
    {
        bool  muted    = ch.Endpoint.Mute || !ch.Endpoint.Available;
        float level    = ch.Endpoint.Level;
        bool  silenced = muted || level <= 0.001f;   // muted, no device, or level at zero

        ch.Glyph.Symbol = ch.IsSpeaker
            ? (muted              ? SymbolRegular.SpeakerMute24      // crossed-out: explicitly muted
               : level <= 0.001f  ? SymbolRegular.Speaker024        // no waves: turned all the way down
               : level < 0.5f     ? SymbolRegular.Speaker124
               :                    SymbolRegular.Speaker224)
            : (silenced ? SymbolRegular.MicOff24 : SymbolRegular.Mic24);

        // Modern muted/silenced cue: keep the icon but fade it back.
        ch.Glyph.Opacity = silenced ? 0.4 : 1.0;
    }

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }
}
