using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterBarApp.Controls;

/// <summary>
/// Lays bar items out left-to-right with these sizing rules:
///   1. Every non-grow item is sized to its content (its natural/desired width).
///   2. If the items' total exceeds the available width, all <b>shrinkable</b> items are
///      scaled down proportionally (to their natural width) until everything fits — with no
///      minimum, so they can collapse as far as needed.
///   3. The single <b>grow</b> item (if any) then expands to consume whatever width is left.
///
/// "Grow" and "Shrinkable" are attached properties set by PanelWindow from the item model.
/// </summary>
public sealed class BarItemsPanel : Panel
{
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(bool), typeof(BarItemsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetGrow(UIElement e, bool v) => e.SetValue(GrowProperty, v);
    public static bool GetGrow(UIElement e) => (bool)e.GetValue(GrowProperty);

    public static readonly DependencyProperty ShrinkableProperty = DependencyProperty.RegisterAttached(
        "Shrinkable", typeof(bool), typeof(BarItemsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetShrinkable(UIElement e, bool v) => e.SetValue(ShrinkableProperty, v);
    public static bool GetShrinkable(UIElement e) => (bool)e.GetValue(ShrinkableProperty);

    private double[] _naturals = [];

    /// <summary>
    /// Force the owning <see cref="BarItemsPanel"/> to re-run its measure after <paramref name="child"/>
    /// changed its own desired width in place (grew/shrank a Canvas, button count, etc.).
    ///
    /// The panel measures each child unconstrained (to learn its natural width) and then re-measures it
    /// at the finite width it allocated — and WPF caches that finite constraint. So a child that later
    /// changes its own width and invalidates only ITS measure is re-measured against the stale finite
    /// width, reports an unchanged DesiredSize, and the panel never reflows. Invalidating the panel
    /// itself makes its unconstrained first pass re-read the new natural width.
    /// </summary>
    public static void InvalidateForChild(DependencyObject child)
    {
        DependencyObject p = child;
        while ((p = VisualTreeHelper.GetParent(p)) != null)
            if (p is BarItemsPanel panel) { panel.InvalidateMeasure(); return; }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        int count = children.Count;
        double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        // 1. Natural (content) widths — measure each unconstrained in width.
        _naturals = new double[count];
        double maxChildHeight = 0;
        for (int i = 0; i < count; i++)
        {
            children[i].Measure(new Size(double.PositiveInfinity, h));
            _naturals[i] = children[i].DesiredSize.Width;
            maxChildHeight = Math.Max(maxChildHeight, children[i].DesiredSize.Height);
        }

        double availW = availableSize.Width;
        var finals = ComputeFinals(children, _naturals, availW);

        // 2. Re-measure each child at the width it will actually get.
        double used = 0;
        for (int i = 0; i < count; i++)
        {
            children[i].Measure(new Size(finals[i], h));
            used += finals[i];
        }

        double width  = double.IsInfinity(availW) ? used : availW;
        double height = double.IsInfinity(availableSize.Height) ? maxChildHeight : availableSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        var finals = ComputeFinals(children, _naturals, finalSize.Width);

        double x = 0;
        for (int i = 0; i < children.Count; i++)
        {
            double w = finals.Length > i ? finals[i] : 0;
            children[i].Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        return finalSize;
    }

    private static double[] ComputeFinals(UIElementCollection children, double[] naturals, double availW)
    {
        int count = children.Count;
        var finals = new double[count];
        Array.Copy(naturals, finals, Math.Min(naturals.Length, count));

        if (double.IsInfinity(availW)) return finals;   // unconstrained: keep content widths

        int growIdx = -1;
        double fixedW = 0, shrinkNatural = 0;
        for (int i = 0; i < count; i++)
        {
            if (growIdx < 0 && GetGrow(children[i])) { growIdx = i; continue; }
            if (GetShrinkable(children[i])) shrinkNatural += naturals[i];
            else                            fixedW       += naturals[i];
        }

        double totalNonGrow = fixedW + shrinkNatural;

        if (totalNonGrow <= availW)
        {
            // Everything fits at content size; the grow item soaks up the remainder.
            if (growIdx >= 0) finals[growIdx] = Math.Max(0, availW - totalNonGrow);
        }
        else
        {
            // Overflow: fixed items keep their content width; shrinkables scale down to fit.
            double availForShrink = Math.Max(0, availW - fixedW);
            double scale = shrinkNatural > 0 ? Math.Min(1.0, availForShrink / shrinkNatural) : 1.0;
            for (int i = 0; i < count; i++)
            {
                if (i == growIdx) { finals[i] = 0; continue; }
                if (GetShrinkable(children[i])) finals[i] = naturals[i] * scale;
            }
        }
        return finals;
    }
}
