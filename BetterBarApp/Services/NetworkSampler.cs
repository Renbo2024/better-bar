using System.Net.NetworkInformation;

namespace BetterBarApp.Services;

/// <summary>
/// Samples receive/send throughput (bytes per second) of one network interface from the
/// cumulative byte counters in <see cref="IPInterfaceStatistics"/>, dividing the delta by
/// the elapsed wall-clock time between calls. Counter resets (interface bounce) and a
/// missing interface are handled by re-priming and reporting 0 rather than throwing.
/// </summary>
public sealed class NetworkSampler
{
    private long     _prevRx, _prevTx;
    private DateTime _prevTime;
    private bool     _primed;
    private string?  _id;

    public double LastReceiveBytesPerSec { get; private set; }
    public double LastSendBytesPerSec    { get; private set; }

    /// <summary>The interface to sample (NetworkInterface.Id). Changing it re-primes so the
    /// next sample's delta isn't computed across two different adapters.</summary>
    public string? InterfaceId
    {
        get => _id;
        set { if (_id != value) { _id = value; _primed = false; } }
    }

    /// <summary>A selectable interface: stable <see cref="Id"/> plus a friendly display name.</summary>
    public sealed record Nic(string Id, string Name);

    /// <summary>Enumerates the selectable (non-loopback) interfaces, current state in the name.</summary>
    public static IReadOnlyList<Nic> List()
    {
        var list = new List<Nic>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var suffix = ni.OperationalStatus == OperationalStatus.Up ? "" : $" ({ni.OperationalStatus})";
                list.Add(new Nic(ni.Id, ni.Name + suffix));
            }
        }
        catch { /* enumeration can fail transiently → return what we have */ }
        return list;
    }

    /// <summary>Returns (receive, send) bytes/sec since the previous call (0 on the priming call,
    /// when the interface is gone, or when no time has elapsed).</summary>
    public (double receiveBps, double sendBps) Sample()
    {
        var ni = Find(_id);
        long rx, tx;
        try
        {
            if (ni == null) throw new InvalidOperationException();
            var s = ni.GetIPStatistics();
            rx = s.BytesReceived; tx = s.BytesSent;
        }
        catch
        {
            _primed = false;
            LastReceiveBytesPerSec = LastSendBytesPerSec = 0;
            return (0, 0);
        }

        var now = DateTime.UtcNow;
        if (!_primed)
        {
            _prevRx = rx; _prevTx = tx; _prevTime = now; _primed = true;
            LastReceiveBytesPerSec = LastSendBytesPerSec = 0;
            return (0, 0);
        }

        double dt = (now - _prevTime).TotalSeconds;
        _prevTime = now;
        double rcv  = dt > 0 ? Math.Max(0, rx - _prevRx) / dt : 0;
        double send = dt > 0 ? Math.Max(0, tx - _prevTx) / dt : 0;
        _prevRx = rx; _prevTx = tx;
        LastReceiveBytesPerSec = rcv; LastSendBytesPerSec = send;
        return (rcv, send);
    }

    private static NetworkInterface? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.Id == id) return ni;
        }
        catch { }
        return null;
    }
}

/// <summary>Formats a bit-rate into a compact "12.3 Mbps"-style string (matches the
/// bandwidth grid units). Sub-Mbps rates fall back to Kbps / bps.</summary>
public static class RateText
{
    public enum Unit { Bps, Kbps, Mbps, Gbps }

    /// <summary>The unit a value would naturally use, by magnitude.</summary>
    public static Unit NaturalUnit(double bitsPerSec) =>
        bitsPerSec >= 1e9 ? Unit.Gbps :
        bitsPerSec >= 1e6 ? Unit.Mbps :
        bitsPerSec >= 1e3 ? Unit.Kbps : Unit.Bps;

    /// <summary>Renders a value in a SPECIFIC unit (so a caller can hold the unit steady).</summary>
    public static string Render(double bitsPerSec, Unit unit) => unit switch
    {
        Unit.Gbps => $"{bitsPerSec / 1e9:0.0} Gbps",
        Unit.Mbps => $"{bitsPerSec / 1e6:0.0} Mbps",
        Unit.Kbps => $"{bitsPerSec / 1e3:0.0} Kbps",
        _         => $"{bitsPerSec:0} bps",
    };

    public static string Bits(double bitsPerSec) => Render(bitsPerSec, NaturalUnit(bitsPerSec));
}

/// <summary>
/// Stateful bit-rate formatter that holds its display unit steady: once it picks a unit
/// (e.g. Mbps) it won't switch to another (e.g. Gbps) until at least <see cref="MinSwitch"/>
/// has elapsed, so a value hovering near a unit boundary doesn't flicker between units.
/// </summary>
public sealed class RateFormatter
{
    private static readonly TimeSpan MinSwitch = TimeSpan.FromSeconds(5);

    private RateText.Unit _unit;
    private bool _have;
    private DateTime _lastSwitch;

    public string Format(double bitsPerSec, DateTime now)
    {
        var natural = RateText.NaturalUnit(bitsPerSec);
        if (!_have)
        {
            _unit = natural; _have = true; _lastSwitch = now;
        }
        else if (natural != _unit && now - _lastSwitch >= MinSwitch)
        {
            _unit = natural; _lastSwitch = now;
        }
        return RateText.Render(bitsPerSec, _unit);
    }
}
