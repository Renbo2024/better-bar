using System.IO;
using BetterBarApp.Models;
using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class ConfigTransferTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bbtest_{Guid.NewGuid():N}.bbconfig");

    [Fact]
    public void Bundle_roundtrips_themes_definitions_items_and_placements()
    {
        var bundle = new ConfigBundle();
        bundle.Themes.Add(new ThemeEntry
        {
            Name = "My Theme",
            Values = new() { ["Accent"] = "#FF112233", ["TaskButtonCornerRadius"] = "4" },
        });

        var def = new DefinitionEntry { Id = Guid.NewGuid(), Name = "Bar A", HeightPx = 44 };
        def.Items.Add(new ClockItem { Title = "Clk" });
        def.Items.Add(new TaskButtonsItem { Rows = 2 });
        def.Placements.Add(new PlacementEntry { Position = PanelPosition.Top, MonitorNumber = 1, WasEnabled = true });
        bundle.Definitions.Add(def);

        ConfigTransferService.Write(bundle, _path);
        var read = ConfigTransferService.Read(_path);

        Assert.NotNull(read);
        var theme = Assert.Single(read!.Themes);
        Assert.Equal("My Theme", theme.Name);
        Assert.Equal("#FF112233", theme.Values["Accent"]);

        var rdef = Assert.Single(read.Definitions);
        Assert.Equal(def.Id, rdef.Id);
        Assert.Equal("Bar A", rdef.Name);
        Assert.Equal(44, rdef.HeightPx);

        // Polymorphic items survive the trip with the right concrete types.
        Assert.IsType<ClockItem>(rdef.Items[0]);
        Assert.Equal("Clk", ((ClockItem)rdef.Items[0]).Title);
        Assert.Equal(2, ((TaskButtonsItem)rdef.Items[1]).Rows);

        var p = Assert.Single(rdef.Placements);
        Assert.Equal(PanelPosition.Top, p.Position);
        Assert.Equal(1, p.MonitorNumber);
        Assert.True(p.WasEnabled);
    }

    [Fact]
    public void Read_rejects_non_bundle_json()
    {
        File.WriteAllText(_path, "{ \"hello\": \"world\" }");   // valid JSON, wrong format marker
        Assert.Null(ConfigTransferService.Read(_path));
    }

    [Fact]
    public void Read_rejects_garbage()
    {
        File.WriteAllText(_path, "not json at all <<<");
        Assert.Null(ConfigTransferService.Read(_path));
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
