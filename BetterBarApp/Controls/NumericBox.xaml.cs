using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterBarApp.Controls;

public partial class NumericBox : UserControl
{
    // ── Dependency properties ───────────────────────────────────────────────────

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumericBox),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumericBox),
            new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumericBox),
            new PropertyMetadata(9999));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(int), typeof(NumericBox),
            new PropertyMetadata(1));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Step    { get => (int)GetValue(StepProperty);    set => SetValue(StepProperty, value); }

    // Routed event so XAML can wire ValueChanged="Handler" (handler takes RoutedEventArgs).
    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NumericBox));

    public event RoutedEventHandler ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    // ── Internal state ──────────────────────────────────────────────────────────

    private bool _suppressTextChanged;

    public NumericBox()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncText();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var nb = (NumericBox)d;
        nb.SyncText();
        nb.RaiseEvent(new RoutedEventArgs(ValueChangedEvent));
    }

    private void SyncText()
    {
        _suppressTextChanged = true;
        ValueBox.Text        = Value.ToString();
        _suppressTextChanged = false;
    }

    // ── Interaction ─────────────────────────────────────────────────────────────

    private void UpBtn_Click(object sender, RoutedEventArgs e) =>
        Value = Math.Clamp(Value + Step, Minimum, Maximum);

    private void DownBtn_Click(object sender, RoutedEventArgs e) =>
        Value = Math.Clamp(Value - Step, Minimum, Maximum);

    private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;
        if (int.TryParse(ValueBox.Text, out int v))
            Value = Math.Clamp(v, Minimum, Maximum);
    }

    private void ValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            Value   = Math.Clamp(Value + Step, Minimum, Maximum);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Value   = Math.Clamp(Value - Step, Minimum, Maximum);
            e.Handled = true;
        }
    }

    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush RestBorder  = new(Color.FromRgb(0x5A, 0x5A, 0x5A));

    private void ValueBox_GotFocus(object sender, RoutedEventArgs e)
    {
        OuterBorder.BorderBrush     = AccentBrush;
        OuterBorder.BorderThickness = new Thickness(2);
        ValueBox.Dispatcher.BeginInvoke(ValueBox.SelectAll);
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        OuterBorder.BorderBrush     = RestBorder;
        OuterBorder.BorderThickness = new Thickness(1);
        SyncText();
    }
}
