using BetterBarApp.Models;
using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class SettingsServiceCloneTests
{
    [Fact]
    public void Clone_preserves_type_and_properties_and_is_independent()
    {
        var original = new TaskButtonsItem
        {
            Rows = 3, MaxButtonWidth = 200, PriorityOrder = "firefox, code", AccentColor = "#FF112233",
        };

        var clone = Assert.IsType<TaskButtonsItem>(SettingsService.CloneItem(original));

        Assert.NotSame(original, clone);
        Assert.Equal(ItemTypes.TaskButtons, clone.TypeKey);
        Assert.Equal(3, clone.Rows);
        Assert.Equal(200, clone.MaxButtonWidth);
        Assert.Equal("firefox, code", clone.PriorityOrder);
        Assert.Equal("#FF112233", clone.AccentColor);

        clone.Rows = 99;
        Assert.Equal(3, original.Rows);   // deep copy — original unaffected
    }

    [Fact]
    public void Clone_handles_nested_polymorphic_widgets()
    {
        var item = new SystemMonitorItem { Spacing = 12 };
        item.Widgets.Add(new CpuMonitorWidget { Title = "cpu", Color = "#FF00FF00", OpacityPercent = 55 });
        item.Widgets.Add(new NetworkMonitorWidget
        {
            Title = "net", InterfaceId = "eth-id", ReceiveColor = "#FF010203",
            ShowBand10G = false, ShowAverage = true, AverageColor = "#80FFFFFF",
        });

        var clone = Assert.IsType<SystemMonitorItem>(SettingsService.CloneItem(item));

        Assert.Equal(12, clone.Spacing);
        Assert.Equal(2, clone.Widgets.Count);
        var w = Assert.IsType<CpuMonitorWidget>(clone.Widgets[0]);
        Assert.Equal("cpu", w.Title);
        Assert.Equal("#FF00FF00", w.Color);
        Assert.Equal(55, w.OpacityPercent);
        Assert.NotSame(item.Widgets[0], clone.Widgets[0]);

        var net = Assert.IsType<NetworkMonitorWidget>(clone.Widgets[1]);
        Assert.Equal("net", net.Title);
        Assert.Equal("eth-id", net.InterfaceId);
        Assert.Equal("#FF010203", net.ReceiveColor);
        Assert.False(net.ShowBand10G);
        Assert.True(net.ShowAverage);
        Assert.Equal("#80FFFFFF", net.AverageColor);
        Assert.NotSame(item.Widgets[1], clone.Widgets[1]);
    }

    [Fact]
    public void Clone_preserves_clock_settings()
    {
        var clone = Assert.IsType<ClockItem>(SettingsService.CloneItem(new ClockItem { Title = "Local" }));
        Assert.Equal("Local", clone.Title);
        Assert.Equal(ItemTypes.Clock, clone.TypeKey);
    }
}

public class PanelManagerCloneTests
{
    [Fact]
    public void CloneDefinition_makes_an_independent_copy_with_a_new_id()
    {
        var source = new BarDefinition { Name = "Original", HeightPx = 40 };
        source.Items.Add(new ClockItem { Title = "T" });
        PanelManager.AddDefinition(source);

        BarDefinition? clone = null;
        try
        {
            clone = PanelManager.CloneDefinition(source);

            Assert.NotEqual(source.Id, clone.Id);
            Assert.Equal("Original (Copy)", clone.Name);
            Assert.Equal(40, clone.HeightPx);

            var clonedClock = Assert.IsType<ClockItem>(Assert.Single(clone.Items));
            Assert.NotSame(source.Items[0], clonedClock);

            clonedClock.Title = "Changed";
            Assert.Equal("T", ((ClockItem)source.Items[0]).Title);   // independent
        }
        finally
        {
            if (clone != null) PanelManager.RemoveDefinition(clone);
            PanelManager.RemoveDefinition(source);
        }
    }
}
