using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterBarApp.Controls;

/// <summary>
/// A button that opens a full RGB/HSV color picker (PixiEditor.ColorPicker: spectrum +
/// sliders + numeric/hex input). <see cref="Value"/> is either an empty string (use the
/// default/theme color) or a hex "#RRGGBB". Raises <see cref="ValueChanged"/> (routed) so
/// XAML can wire it to a handler.
/// </summary>
public partial class ColorPickerButton : UserControl
{
    private bool _suppress;

    public ColorPickerButton() => InitializeComponent();

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata("", OnValuePropertyChanged));

    /// <summary>"" = default/theme colour; otherwise "#RRGGBB" (or "#AARRGGBB" when ShowAlpha).</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>When true, includes an alpha channel (slider + #AARRGGBB output). Default false.</summary>
    public static readonly DependencyProperty ShowAlphaProperty = DependencyProperty.Register(
        nameof(ShowAlpha), typeof(bool), typeof(ColorPickerButton), new FrameworkPropertyMetadata(false));

    public bool ShowAlpha
    {
        get => (bool)GetValue(ShowAlphaProperty);
        set => SetValue(ShowAlphaProperty, value);
    }

    /// <summary>When true (default), offers a "use theme default" option that yields "". When false,
    /// the value is always a concrete colour (used by the theme editor, where every key has a value).</summary>
    public static readonly DependencyProperty AllowDefaultProperty = DependencyProperty.Register(
        nameof(AllowDefault), typeof(bool), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata(true, (d, _) => ((ColorPickerButton)d).RefreshFromValue()));

    public bool AllowDefault
    {
        get => (bool)GetValue(AllowDefaultProperty);
        set => SetValue(AllowDefaultProperty, value);
    }

    // Bound TwoWay to the picker's SelectedColor.
    public static readonly DependencyProperty PickedColorProperty = DependencyProperty.Register(
        nameof(PickedColor), typeof(Color), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata(Colors.Gray, OnPickedColorChanged));

    public Color PickedColor
    {
        get => (Color)GetValue(PickedColorProperty);
        set => SetValue(PickedColorProperty, value);
    }

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ColorPickerButton));

    public event RoutedEventHandler ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorPickerButton)d).RefreshFromValue();

    private static void OnPickedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ColorPickerButton)d;
        if (c._suppress || c.DefaultBox.IsChecked == true) return;
        c.Commit(c.HexOf((Color)e.NewValue));
    }

    private void DefaultBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        Commit(DefaultBox.IsChecked == true ? "" : HexOf(PickedColor));
    }

    private void Commit(string v)
    {
        SetCurrentValue(ValueProperty, v);   // → RefreshFromValue updates the UI
        RaiseEvent(new RoutedEventArgs(ValueChangedEvent));
    }

    // Reflect Value in the UI without echoing back through the change handlers.
    private void RefreshFromValue()
    {
        _suppress = true;
        var c = ParseColor(Value);
        bool isDefault = AllowDefault && c == null;

        DefaultBox.IsChecked = isDefault;
        Picker.IsEnabled     = !isDefault;

        var col = c ?? Colors.Gray;
        PickedColor = col;

        if (isDefault)
        {
            Swatch.Background = Brushes.Transparent;
            Label.Text = "Default";
        }
        else
        {
            Swatch.Background = new SolidColorBrush(col);
            Label.Text = HexOf(col);
        }
        _suppress = false;
    }

    private string HexOf(Color c) =>
        ShowAlpha ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }
}
