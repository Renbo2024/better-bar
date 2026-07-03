using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterBarApp.Services;
using Wpf.Ui.Controls;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace BetterBarApp.Windows;

/// <summary>
/// Flyout shown above an Audio Control icon: a device picker (select the active device) and a level
/// slider. For the speaker, a short confirmation tone plays when the slider is released. Closes when
/// it loses focus or Escape is pressed.
/// </summary>
public partial class AudioFlyoutWindow : Window
{
    private static AudioFlyoutWindow? _open;

    private readonly AudioEndpoint    _endpoint;
    private readonly bool             _isSpeaker;
    private readonly FrameworkElement _anchor;
    private bool _loading;
    private bool _positioned;

    public static void ShowFor(AudioEndpoint endpoint, bool isSpeaker, FrameworkElement anchor)
    {
        _open?.Close();
        var w = new AudioFlyoutWindow(endpoint, isSpeaker, anchor);
        _open = w;
        w.Show();
        w.Activate();
    }

    private AudioFlyoutWindow(AudioEndpoint endpoint, bool isSpeaker, FrameworkElement anchor)
    {
        _endpoint  = endpoint;
        _isSpeaker = isSpeaker;
        _anchor    = anchor;
        InitializeComponent();

        Left = -10000; Top = -10000; Opacity = 0;   // hidden until positioned (see OnContentRendered)
        HeaderText.Text = isSpeaker ? "Output device" : "Input device";

        Loaded      += OnLoaded;
        Deactivated += (_, _) => Close();
        Closed      += (_, _) => { if (_open == this) _open = null; };
        KeyDown     += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // A short tone confirms a speaker level change once the slider is released (Windows-style).
        LevelSlider.PreviewMouseLeftButtonUp += (_, _) => { if (_isSpeaker) AudioService.PlayTone(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var devices = _endpoint.Devices();
        DeviceList.ItemsSource  = devices;
        DeviceList.SelectedItem = devices.FirstOrDefault(d => d.Id == _endpoint.DefaultId);
        LevelSlider.Value = Math.Round(_endpoint.Level * 100);
        UpdateMuteButton();
        _loading = false;
        // Positioning happens in OnContentRendered, once SizeToContent has produced the final size.
    }

    // Pre-move onto the anchor's monitor before SizeToContent measures, so the size is right on
    // mixed-DPI setups (see FlyoutPlacement). The window stays hidden (Opacity 0) until then.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FlyoutPlacement.MoveToAnchorMonitor(this, _anchor);
    }

    // By now SizeToContent has finalized the window size, so placement is accurate; reveal once placed.
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_positioned) return;
        _positioned = true;
        FlyoutPlacement.Place(this, _anchor, gap: 8);
        Opacity = 1;
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DeviceList.SelectedItem is AudioDeviceInfo d) _endpoint.SetDefault(d.Id);
    }

    private void Level_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _endpoint.Level = (float)(LevelSlider.Value / 100.0);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _endpoint.Mute = !_endpoint.Mute;
        UpdateMuteButton();
    }

    private void UpdateMuteButton()
    {
        var symbol = _endpoint.Mute
            ? (_isSpeaker ? SymbolRegular.SpeakerMute24 : SymbolRegular.MicOff24)
            : (_isSpeaker ? SymbolRegular.Speaker224    : SymbolRegular.Mic24);
        MuteButton.Icon = new SymbolIcon { Symbol = symbol };
    }
}
