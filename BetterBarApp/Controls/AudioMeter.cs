using System.Windows;
using System.Windows.Media;

namespace BetterBarApp.Controls;

/// <summary>
/// A horizontal level meter that starts as a centre dot and extends LEFT for the left-channel level
/// and RIGHT for the right-channel level, up to half the element's width each way (so the full bar
/// spans the icon's width). Transparent background; configurable thickness and colour.
///
/// It's an activity indicator, not a precise meter, so the displayed level is shaped two ways:
///  • <b>Smoothing</b> — an exponential glide that damps jitter (same factor for both channels).
///  • <b>Auto-scale</b> — the raw peaks are divided by a running maximum that jumps up to new peaks
///    and decays slowly, so the bar uses the full width and adapts to louder/softer activity over
///    time. Both channels are divided by the <i>same</i> running max, so L vs R differences show.
/// </summary>
public sealed class AudioMeter : FrameworkElement
{
    // Below this the running max is treated as silence, so ambient noise doesn't peg an auto-scaled
    // meter and we never divide by ~0.
    private const float MinScale = 0.04f;

    private float  _dispL, _dispR;   // smoothed, post-scale values actually drawn (0..1)
    private float  _runningMax;      // adaptive shared scale for auto-scaling
    private double _thickness = 3;
    private Brush  _brush = Brushes.DodgerBlue;

    private double _smoothing;       // 0..~0.9 EMA retention (higher = smoother)
    private bool   _autoScale = true;
    private double _scaleDecay = 0.97; // per-frame running-max decay (lower = adapts faster)

    /// <param name="smoothing">0..1 from the config (0 = instant, 1 = maximally smooth).</param>
    /// <param name="scaleSpeed">0..1 from the config (0 = adapts very slowly, 1 = adapts fast).</param>
    public void Configure(Color color, double thickness, double smoothing, bool autoScale, double scaleSpeed)
    {
        _thickness = Math.Max(1, thickness);
        _brush = new SolidColorBrush(color);
        _brush.Freeze();

        _smoothing  = Math.Clamp(smoothing, 0, 1) * 0.92;      // cap so it never fully freezes
        _autoScale  = autoScale;
        // Faster speed → smaller per-frame retention of the running max → quicker re-adaptation.
        _scaleDecay = 0.999 - Math.Clamp(scaleSpeed, 0, 1) * 0.099;  // ~0.999 (slow) … 0.90 (fast)

        InvalidateVisual();
    }

    public void SetLevels(float left, float right)
    {
        float l = Math.Max(0f, left), r = Math.Max(0f, right);

        if (_autoScale)
        {
            // Running max jumps up to new peaks instantly, then decays so the meter grows more
            // sensitive again during quieter passages. Both channels share this one scale.
            float peak = Math.Max(l, r);
            _runningMax = Math.Max(_runningMax * (float)_scaleDecay, peak);
            float scale = Math.Max(_runningMax, MinScale);
            l /= scale;
            r /= scale;
        }

        l = Math.Clamp(l, 0f, 1f);
        r = Math.Clamp(r, 0f, 1f);

        // Exponential smoothing toward the (scaled) target — identical factor for both channels.
        _dispL = (float)(_dispL * _smoothing + l * (1 - _smoothing));
        _dispR = (float)(_dispR * _smoothing + r * (1 - _smoothing));

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double cx = w / 2;
        double cy = h / 2;
        double half = w / 2;
        double t = Math.Min(_thickness, h);
        double r = t / 2;

        double x1 = cx - Math.Clamp(_dispL, 0f, 1f) * half;
        double x2 = cx + Math.Clamp(_dispR, 0f, 1f) * half;

        // Never smaller than a centred dot.
        if (x2 - x1 < t) { x1 = cx - r; x2 = cx + r; }

        dc.DrawRoundedRectangle(_brush, null, new Rect(x1, cy - t / 2, x2 - x1, t), r, r);
    }
}
