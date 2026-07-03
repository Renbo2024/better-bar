using System.Windows;
using System.Windows.Media;
using BetterBarApp.Models;

namespace BetterBarApp.Controls;

/// <summary>
/// Draws one interface's throughput: the total (send+receive) as a fill from the bottom, then
/// the receive and send lines on top. The vertical scale is dynamic — 110% of the largest total
/// over the last 5 minutes — so the visible range tracks the link's recent speed and fixed
/// bandwidth grid lines above the current scale simply don't show.
/// Per-sample (snap) and smoothed (continuous) scrolling match <see cref="CpuGraph"/>.
/// All throughput values are bits per second.
/// </summary>
public sealed class NetworkGraph : FrameworkElement
{
    private readonly List<(double Receive, double Send)> _history = new();   // bits/sec

    private MonitorScrollMode _mode = MonitorScrollMode.Smoothed;
    private int _intervalMs = 1000;
    private int _visible = 60;

    private SolidColorBrush _totalBrush = new(Color.FromRgb(0x22, 0xA0, 0xFF)) { Opacity = 0.3 };
    private Pen _receivePen = new(Brushes.LimeGreen, 1);
    private Pen _sendPen    = new(Brushes.Orange, 1);

    private bool _showGrid;
    private Pen? _gridPen;
    private bool _b10M, _b100M, _b1G, _b10G;
    private bool _showAverage;
    private Pen? _avgPen;

    private const int GridCols = 4;   // vertical segments, like CPU
    // Fixed bandwidth grid lines, in bits/sec.
    private const double Mbps10 = 10e6, Mbps100 = 100e6, Gbps1 = 1e9, Gbps10 = 10e9;

    // Totals (with timestamps) over the last WindowSeconds — drives the dynamic scale.
    private readonly Queue<(DateTime Time, double Total)> _window = new();
    private const double WindowSeconds = 300;   // scale = 110% of the max total over the last 5 minutes
    private double _maxTotal;         // largest total within the window
    private double _avgTotal;         // mean total over the visible span
    private int    _sampleParity;     // average is recomputed at most every other sample

    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private bool _renderingHooked;

    /// <summary>Applies all visual settings without clearing samples (live edits redraw at once).</summary>
    public void Configure(Color totalFill, double totalOpacity,
                          Color receiveLine, double receiveOpacity, Color sendLine, double sendOpacity,
                          MonitorScrollMode mode, int intervalMs, int visibleSamples,
                          bool showGrid, Color gridColor, bool b10M, bool b100M, bool b1G, bool b10G,
                          bool showAverage, Color averageColor)
    {
        _mode = mode; _intervalMs = Math.Max(50, intervalMs);
        _visible = Math.Max(2, visibleSamples);

        _totalBrush = new SolidColorBrush(totalFill) { Opacity = Math.Clamp(totalOpacity, 0, 1) };
        _totalBrush.Freeze();
        _receivePen = LinePen(receiveLine, receiveOpacity);
        _sendPen    = LinePen(sendLine,    sendOpacity);

        _showGrid = showGrid;
        _gridPen  = new Pen(new SolidColorBrush(gridColor), 1); _gridPen.Freeze();
        _b10M = b10M; _b100M = b100M; _b1G = b1G; _b10G = b10G;

        _showAverage = showAverage;
        _avgPen = new Pen(new SolidColorBrush(averageColor), 1); _avgPen.Freeze();

        HookRendering(mode == MonitorScrollMode.Smoothed);
        InvalidateVisual();
    }

    private static Pen LinePen(Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }

    public void AddSample(double receiveBitsPerSec, double sendBitsPerSec)
    {
        double total = Math.Max(0, receiveBitsPerSec) + Math.Max(0, sendBitsPerSec);
        _history.Add((Math.Max(0, receiveBitsPerSec), Math.Max(0, sendBitsPerSec)));
        int keep = _visible + 2;
        while (_history.Count > keep) _history.RemoveAt(0);

        // Scale = 110% of the max total over the last 5 minutes: keep a timestamped window,
        // drop entries older than the window, and take its running maximum.
        var now = DateTime.UtcNow;
        _window.Enqueue((now, total));
        var cutoff = now - TimeSpan.FromSeconds(WindowSeconds);
        while (_window.Count > 0 && _window.Peek().Time < cutoff) _window.Dequeue();
        double max = 0;
        foreach (var e in _window) if (e.Total > max) max = e.Total;
        _maxTotal = max;

        // Recompute the span average at most every other sample (per spec).
        if ((++_sampleParity & 1) == 0) RecomputeAverage();

        _lastSampleUtc = now;
        if (_mode == MonitorScrollMode.PerSample) InvalidateVisual();
    }

    public void Stop() => HookRendering(false);

    private void RecomputeAverage()
    {
        int count = Math.Min(_visible, _history.Count);
        if (count == 0) { _avgTotal = 0; return; }
        double sum = 0;
        for (int i = _history.Count - count; i < _history.Count; i++)
            sum += _history[i].Receive + _history[i].Send;
        _avgTotal = sum / count;
    }

    // Top of the scale: 110% of the largest total over the last 5 minutes, with a small floor so
    // an idle link still has a sane axis (and never divides by zero).
    private double ScaleMax => Math.Max(_maxTotal * 1.1, 1.0);

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
        if (e is RenderingEventArgs args)
        {
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
            double scale = ScaleMax;

            // Grid: vertical columns (static) + the enabled bandwidth lines at their real heights.
            if (_showGrid && _gridPen != null)
            {
                for (int i = 1; i < GridCols; i++)
                {
                    double x = w * i / GridCols;
                    dc.DrawLine(_gridPen, new Point(x, 0), new Point(x, h));
                }
                DrawBand(dc, _b10M,  Mbps10,  scale, h, w);
                DrawBand(dc, _b100M, Mbps100, scale, h, w);
                DrawBand(dc, _b1G,   Gbps1,   scale, h, w);
                DrawBand(dc, _b10G,  Gbps10,  scale, h, w);
            }

            if (_history.Count >= 2)
            {
                int    N      = _visible;
                double sw     = N > 1 ? w / (N - 1) : w;
                bool   smooth = _mode == MonitorScrollMode.Smoothed;

                double phase = 0;
                if (smooth)
                    phase = Math.Clamp((DateTime.UtcNow - _lastSampleUtc).TotalMilliseconds / _intervalMs, 0, 1);

                int count = Math.Min(smooth ? N + 1 : N, _history.Count);
                if (count >= 2)
                {
                    int start = _history.Count - count;

                    double X(int j)
                    {
                        double x = w - (count - 1 - j) * sw;
                        if (smooth) x += (1 - phase) * sw;
                        return x;
                    }
                    double Y(double bits) => h * (1 - Math.Clamp(bits / scale, 0, 1));

                    // Total fill (drawn first, from the bottom).
                    var fill = new StreamGeometry();
                    using (var c = fill.Open())
                    {
                        c.BeginFigure(new Point(X(0), h), true, true);
                        for (int j = 0; j < count; j++)
                        {
                            var s = _history[start + j];
                            c.LineTo(new Point(X(j), Y(s.Receive + s.Send)), true, false);
                        }
                        c.LineTo(new Point(X(count - 1), h), true, false);
                    }
                    fill.Freeze();
                    dc.DrawGeometry(_totalBrush, null, fill);

                    // Receive and send lines, over the fill.
                    DrawLine(dc, _receivePen, count, start, X, Y, s => s.Receive);
                    DrawLine(dc, _sendPen,    count, start, X, Y, s => s.Send);
                }
            }

            // Average-over-span reference line, on top.
            if (_showAverage && _avgPen != null && _avgTotal > 0)
            {
                double y = h * (1 - Math.Clamp(_avgTotal / scale, 0, 1));
                dc.DrawLine(_avgPen, new Point(0, y), new Point(w, y));
            }
        }
        finally { dc.Pop(); }
    }

    private void DrawBand(DrawingContext dc, bool on, double bits, double scale, double h, double w)
    {
        if (!on || _gridPen == null || bits > scale) return;   // above the current scale → off-screen
        double y = h * (1 - bits / scale);
        dc.DrawLine(_gridPen, new Point(0, y), new Point(w, y));
    }

    private void DrawLine(DrawingContext dc, Pen pen, int count, int start,
                          Func<int, double> X, Func<double, double> Y,
                          Func<(double Receive, double Send), double> pick)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            var first = _history[start];
            c.BeginFigure(new Point(X(0), Y(pick(first))), false, false);
            for (int j = 1; j < count; j++)
                c.LineTo(new Point(X(j), Y(pick(_history[start + j]))), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
