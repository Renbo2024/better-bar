using System;
using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class RateFormatterTests
{
    [Fact]
    public void First_value_picks_its_natural_unit()
    {
        var f = new RateFormatter();
        Assert.EndsWith("Mbps", f.Format(50e6, DateTime.UtcNow));
    }

    [Fact]
    public void Unit_holds_steady_within_five_seconds()
    {
        var f  = new RateFormatter();
        var t0 = DateTime.UtcNow;
        Assert.EndsWith("Mbps", f.Format(50e6, t0));                       // settles on Mbps

        // A jump into Gbps range less than 5s later keeps Mbps (no flicker).
        var s = f.Format(2e9, t0 + TimeSpan.FromSeconds(4));
        Assert.EndsWith("Mbps", s);
        Assert.Equal("2000.0 Mbps", s);
    }

    [Fact]
    public void Unit_switches_after_five_seconds()
    {
        var f  = new RateFormatter();
        var t0 = DateTime.UtcNow;
        f.Format(50e6, t0);
        Assert.EndsWith("Gbps", f.Format(2e9, t0 + TimeSpan.FromSeconds(5)));
    }
}
