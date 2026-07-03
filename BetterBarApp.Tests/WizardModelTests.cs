using System.Linq;
using BetterBarApp.Models;
using BetterBarApp.Windows;
using Xunit;

namespace BetterBarApp.Tests;

public class WizardModelTests
{
    [Fact]
    public void Default_build_has_no_quick_launch_and_grow_to_fill_task_buttons()
    {
        var def = new WizardModel { HeightPx = 52 }.BuildDefinition();

        Assert.Equal("Main", def.Name);
        Assert.Equal(52, def.HeightPx);

        // Order with all optionals at defaults (Quick Launch off, Audio + Clock on):
        // StartButton, Separator, TaskButtons, Audio, SystemTray, Clock.
        Assert.Collection(def.Items,
            i => Assert.IsType<StartButtonItem>(i),
            i => Assert.IsType<SeparatorItem>(i),
            i => Assert.IsType<TaskButtonsItem>(i),
            i => Assert.IsType<AudioControlItem>(i),
            i => Assert.IsType<SystemTrayItem>(i),
            i => Assert.IsType<ClockItem>(i));

        Assert.True(def.Items.OfType<TaskButtonsItem>().Single().GrowToFill);
        Assert.DoesNotContain(def.Items, i => i is LauncherItem);
    }

    [Fact]
    public void Quick_launch_adds_a_separator_then_launcher_after_the_start_button()
    {
        var def = new WizardModel
        {
            IncludeQuickLaunch = true,
            QuickLaunchFolder  = @"C:\Shortcuts",
            QuickLaunchRows    = 2,
        }.BuildDefinition();

        Assert.IsType<StartButtonItem>(def.Items[0]);
        Assert.IsType<SeparatorItem>(def.Items[1]);
        var launcher = Assert.IsType<LauncherItem>(def.Items[2]);
        Assert.Equal(@"C:\Shortcuts", launcher.SourceDirectory);
        Assert.Equal(2, launcher.Rows);
    }

    [Fact]
    public void Optional_items_are_omitted_when_disabled()
    {
        var def = new WizardModel
        {
            IncludeAudio = false,
            IncludeClock = false,
        }.BuildDefinition();

        Assert.DoesNotContain(def.Items, i => i is AudioControlItem);
        Assert.DoesNotContain(def.Items, i => i is ClockItem);
        // The always-present items remain.
        Assert.Contains(def.Items, i => i is StartButtonItem);
        Assert.Contains(def.Items, i => i is TaskButtonsItem);
        Assert.Contains(def.Items, i => i is SystemTrayItem);
    }

    [Fact]
    public void Toggles_flow_into_the_built_items()
    {
        var def = new WizardModel
        {
            SearchApps       = false,
            SearchDocuments  = true,
            TaskAllMonitors  = true,
            AudioMicrophone  = true,
            Clock24Hour      = true,
            ClockShowSeconds = true,
        }.BuildDefinition();

        var start = def.Items.OfType<StartButtonItem>().Single();
        Assert.False(start.SearchApps);
        Assert.True(start.SearchDocuments);

        Assert.True(def.Items.OfType<TaskButtonsItem>().Single().ShowAllMonitors);
        Assert.True(def.Items.OfType<AudioControlItem>().Single().ShowMicrophone);

        var clock = def.Items.OfType<ClockItem>().Single();
        Assert.True(clock.Use24Hour);
        Assert.True(clock.ShowSeconds);
    }
}
