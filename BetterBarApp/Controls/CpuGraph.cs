using System.Windows;
using System.Windows.Media;
using BetterBarApp.Models;

namespace BetterBarApp.Controls;

/// <summary>
/// Draws per-thread CPU usage as overlapping filled line graphs (one translucent fill per
/// thread; the line itself isn't drawn, so busier CPUs read as a more prominent colour).
/// Transparent background. Supports per-sample (snap) and smoothed (continuous) scrolling.
/// </summary>
public sealed class CpuGraph : FrameworkElement
{
    private readonly List<float[]> _history = new();   // each entry: per-thread usage 0..1
    private int _threadCount = 1;

    private Color _color = Color.FromRgb(0x22, 0xA0, 0xFF);
    private double _opacity = 0.3;
    private MonitorScrollMode _mode = MonitorScrollMode.Smoothed;
    private int _intervalMs = 1000;
    private int _visible = 60;

    private SolidColorBrush _brush = new(Color.FromRgb(0x22, 0xA0, 0xFF)) { Opacity = 0.3 };
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private bool _renderingHooked;

    private bool _showGrid;
    private Pen? _gridPen;
    private const int GridRows = 10;   // segments bottom→top
    private const int GridCols = 4;    // segments left→right

    /// <summary>
    /// Applies all visual settings. Does NOT clear the sample buffer, so changes are reflected
    /// by re-drawing the existing history immediately rather than restarting the graph.
    /// </summary>
    public void Configure(Color color, double opacity, MonitorScrollMode mode, int intervalMs,
                          int visibleSamples, bool showGrid, Color gridColor)
    {
        _color = color; _opacity = Math.Clamp(opacity, 0, 1);
        _mode = mode; _intervalMs = Math.Max(50, intervalMs);
        _visible = Math.Max(2, visibleSamples);
        _brush = new SolidColorBrush(_color) { Opacity = _opacity };
        _brush.Freeze();

        _showGrid = showGrid;
        _gridPen  = new Pen(new SolidColorBrush(gridColor), 1);
        _gridPen.Freeze();

        HookRendering(mode == MonitorScrollMode.Smoothed);
        InvalidateVisual();
    }

    public void SetThreadCount(int n) => _threadCount = Math.Max(1, n);

    public void AddSample(float[] perThread)
    {
        _history.Add((float[])perThread.Clone());
        int keep = _visible + 2;
        while (_history.Count > keep) _history.RemoveAt(0);
        _lastSampleUtc = DateTime.UtcNow;
        if (_mode == MonitorScrollMode.PerSample) InvalidateVisual();
        // Smoothed mode repaints every frame via the rendering hook.
    }

    public void Stop() => HookRendering(false);

    private void HookRendering(bool on)
    {
        if (on && !_renderingHooked) { CompositionTarget.Rendering += OnRendering; _renderingHooked = true; }
        else if (!on && _renderingHooked) { CompositionTarget.Rendering -= OnRendering; _renderingHooked = false; }
    }

    private TimeSpan _lastRenderTime;
    private bool _haveRendered;
    private static readonly TimeSpan MinRenderInterval = TimeSpan.FromMilliseconds(33);   // ~30 fps

    private void OnRendering(object? sender, EventArgs e)
    {
        // CompositionTarget.Rendering fires once per display frame (60–144 Hz). Smoothed scrolling reads
        // as fluid at ~30 fps, so cap the repaint rate: on high-refresh displays and many-core machines
        // (one filled geometry is rebuilt per logical processor every paint) this is a large CPU saving.
        if (e is RenderingEventArgs args)
        {
            // Only diff two real RenderingTime values (never a sentinel — TimeSpan.MinValue overflows).
            if (_haveRendered && args.RenderingTime - _lastRenderTime < MinRenderInterval) return;
            _lastRenderTime = args.RenderingTime;
            _haveRendered = true;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        try
        {
            // Static grid behind the fills — fixed positions, never scrolls with the data.
            if (_showGrid && _gridPen != null)
            {
                for (int i = 1; i < GridRows; i++)
                {
                    double y = h * i / GridRows;
                    dc.DrawLine(_gridPen, new Point(0, y), new Point(w, y));
                }
                for (int i = 1; i < GridCols; i++)
                {
                    double x = w * i / GridCols;
                    dc.DrawLine(_gridPen, new Point(x, 0), new Point(x, h));
                }
            }

            if (_history.Count < 2) return;

            int    N      = _visible;
            double sw     = N > 1 ? w / (N - 1) : w;
            bool   smooth = _mode == MonitorScrollMode.Smoothed;

            double phase = 0;
            if (smooth)
                phase = Math.Clamp((DateTime.UtcNow - _lastSampleUtc).TotalMilliseconds / _intervalMs, 0, 1);

            int count = Math.Min(smooth ? N + 1 : N, _history.Count);
            if (count < 2) return;
            int start = _history.Count - count;

            // x of render index j (0 = oldest shown, count-1 = newest).
            double X(int j)
            {
                double x = w - (count - 1 - j) * sw;
                if (smooth) x += (1 - phase) * sw;   // scroll left over the sample interval
                return x;
            }

            for (int t = 0; t < _threadCount; t++)
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(new Point(X(0), h), true, true);   // baseline at oldest x
                    for (int j = 0; j < count; j++)
                    {
                        var sample = _history[start + j];
                        float u = t < sample.Length ? sample[t] : 0f;
                        c.LineTo(new Point(X(j), h * (1 - u)), true, false);
                    }
                    c.LineTo(new Point(X(count - 1), h), true, false); // baseline at newest x
                }
                geo.Freeze();
                dc.DrawGeometry(_brush, null, geo);
            }
        }
        finally { dc.Pop(); }
    }
}
