using BetterBarApp.Models;
using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class ItemTypeRegistryTests
{
    [Fact]
    public void Every_factory_produces_an_item_with_the_registered_key()
    {
        Assert.NotEmpty(ItemTypeRegistry.Types);
        foreach (var t in ItemTypeRegistry.Types)
        {
            var item = t.Factory();
            Assert.NotNull(item);
            Assert.Equal(t.Key, item.TypeKey);
            Assert.False(string.IsNullOrWhiteSpace(t.DisplayName));
        }
    }

    [Fact]
    public void Keys_are_unique()
    {
        var keys = ItemTypeRegistry.Types.Select(t => t.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData(ItemTypes.Launcher)]
    [InlineData(ItemTypes.TaskButtons)]
    [InlineData(ItemTypes.Separator)]
    [InlineData(ItemTypes.StartButton)]
    [InlineData(ItemTypes.Clock)]
    [InlineData(ItemTypes.SystemMonitor)]
    [InlineData(ItemTypes.AudioControl)]
    [InlineData(ItemTypes.SystemTray)]
    [InlineData(ItemTypes.Power)]
    [InlineData(ItemTypes.Weather)]
    public void Known_types_are_registered(string key)
        => Assert.Contains(ItemTypeRegistry.Types, t => t.Key == key);
}

public class ThemeSchemaTests
{
    [Fact]
    public void Keys_are_non_empty_and_unique()
    {
        Assert.NotEmpty(ThemeSchema.Keys);
        var keys = ThemeSchema.Keys.Select(k => k.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Find_resolves_known_keys_and_rejects_unknown()
    {
        Assert.NotNull(ThemeSchema.Find("Accent"));
        Assert.Equal("Accent", ThemeSchema.Find("Accent")!.Key);
        Assert.Null(ThemeSchema.Find("NoSuchKey"));
    }

    [Fact]
    public void Corner_radius_is_the_only_non_color_key()
    {
        var nonColor = ThemeSchema.Keys.Where(k => k.Kind == ThemeKeyKind.CornerRadius).ToList();
        Assert.Single(nonColor);
        Assert.Equal("TaskButtonCornerRadius", nonColor[0].Key);
    }
}
