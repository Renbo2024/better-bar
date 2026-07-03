using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;

namespace BetterBarApp.Controls;

/// <summary>
/// Lays out a <see cref="SystemMonitorItem"/>'s widgets left-to-right with the configured
/// spacing between them and outer side / top-bottom margins. Spacing and margin changes are
/// applied in place; the individual widget controls apply their own setting changes live.
/// </summary>
public partial class SystemMonitorControl : UserControl
{
    private readonly SystemMonitorItem _item;

    public SystemMonitorControl(SystemMonitorItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _item.PropertyChanged += OnItemChanged;
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _item.PropertyChanged -= OnItemChanged;

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemMonitorItem.Spacing)
                           or nameof(SystemMonitorItem.SideMargin)
                           or nameof(SystemMonitorItem.VerticalMargin))
            ApplyLayout();
    }

    private void Rebuild()
    {
        Host.Children.Clear();
        foreach (var widget in _item.Widgets)
        {
            FrameworkElement? w = widget switch
            {
                CpuMonitorWidget cpu => new CpuMonitorWidgetControl(cpu),
                NetworkMonitorWidget net => new NetworkMonitorWidgetControl(net),
                _                    => null,
            };
            if (w != null) Host.Children.Add(w);
        }
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        Outer.Margin = new Thickness(_item.SideMargin, _item.VerticalMargin, _item.SideMargin, _item.VerticalMargin);
        for (int i = 0; i < Host.Children.Count; i++)
            if (Host.Children[i] is FrameworkElement fe)
                fe.Margin = i > 0 ? new Thickness(_item.Spacing, 0, 0, 0) : new Thickness();

        // Spacing / margins / widget set changed our natural width; make the owning panel
        // re-measure so the item's bar footprint grows or shrinks (see BarItemsPanel.InvalidateForChild).
        BarItemsPanel.InvalidateForChild(this);
    }
}
