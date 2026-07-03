using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;

namespace BetterBarApp.Controls;

public partial class SeparatorControl : UserControl
{
    private const double LineThickness = 1;
    private readonly SeparatorItem _item;

    public SeparatorControl(SeparatorItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Line.Visibility = _item.Visible ? Visibility.Visible : Visibility.Collapsed;

        if (_item.GrowToFill)
        {
            // Panel gives this a star column; fill it. The line (if visible) is
            // centered in the filled space — invisible + grow = a pure spacer.
            return;
        }

        // Content-sized: margin on each side of the (optional) line.
        double lineW = _item.Visible ? LineThickness : 0;
        Width = 2 * _item.Margin + lineW;
    }
}
