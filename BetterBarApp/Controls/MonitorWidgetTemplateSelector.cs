using System.Windows;
using System.Windows.Controls;
using BetterBarApp.Models;

namespace BetterBarApp.Controls;

/// <summary>
/// Picks the type-specific settings template for a <see cref="MonitorWidget"/> (the common
/// fields — width, title, subtitle, text colour — live in the outer card template).
/// </summary>
public sealed class MonitorWidgetTemplateSelector : DataTemplateSelector
{
    public DataTemplate? CpuTemplate     { get; set; }
    public DataTemplate? NetworkTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        CpuMonitorWidget     => CpuTemplate,
        NetworkMonitorWidget => NetworkTemplate,
        _                    => null,
    };
}
