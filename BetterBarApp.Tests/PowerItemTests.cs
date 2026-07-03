using BetterBarApp.Models;
using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class PowerItemTests
{
    [Fact]
    public void Description_lists_shown_buttons_in_order()
    {
        var item = new PowerItem { ConfirmAction = false };   // all four+reboot shown by default
        Assert.Equal("Power, Reboot, Hibernate, Sleep, Log Off", item.Description);
    }

    [Fact]
    public void Description_reflects_a_subset_keeping_order()
    {
        var item = new PowerItem { ConfirmAction = false, ShowReboot = false, ShowHibernate = false };
        Assert.Equal("Power, Sleep, Log Off", item.Description);
    }

    [Fact]
    public void Description_handles_nothing_shown()
    {
        var item = new PowerItem
        {
            ConfirmAction = false,
            ShowPower = false, ShowReboot = false, ShowHibernate = false, ShowSleep = false, ShowLogOff = false,
        };
        Assert.Equal("(nothing shown)", item.Description);
    }

    [Fact]
    public void Description_notes_confirm_setting()
    {
        Assert.Contains("confirm before acting", new PowerItem { ConfirmAction = true }.Description);
        Assert.DoesNotContain("confirm", new PowerItem { ConfirmAction = false }.Description);
    }

    [Fact]
    public void Clone_preserves_toggles_and_appearance_and_is_independent()
    {
        var original = new PowerItem
        {
            ShowReboot = false, ShowLabels = true, IconSize = 28, IconSpacing = 12,
            OuterMargin = 10, LabelFontFamily = "Consolas", LabelFontSize = 11, ConfirmAction = false,
        };

        var clone = Assert.IsType<PowerItem>(SettingsService.CloneItem(original));

        Assert.NotSame(original, clone);
        Assert.Equal(ItemTypes.Power, clone.TypeKey);
        Assert.False(clone.ShowReboot);
        Assert.True(clone.ShowLabels);
        Assert.Equal(28, clone.IconSize);
        Assert.Equal(12, clone.IconSpacing);
        Assert.Equal(10, clone.OuterMargin);
        Assert.Equal("Consolas", clone.LabelFontFamily);
        Assert.Equal(11, clone.LabelFontSize);
        Assert.False(clone.ConfirmAction);

        clone.IconSize = 99;
        Assert.Equal(28, original.IconSize);   // deep copy
    }
}

public class SystemTrayItemTests
{
    [Fact]
    public void Description_notes_excluded_icons()
    {
        var item = new SystemTrayItem();   // both excludes default true
        Assert.Contains("no sound icon", item.Description);
        Assert.Contains("no mic icon", item.Description);
    }

    [Fact]
    public void Description_drops_notes_when_includes_enabled()
    {
        var item = new SystemTrayItem { ExcludeSound = false, ExcludeMicrophone = false };
        Assert.DoesNotContain("no sound icon", item.Description);
        Assert.DoesNotContain("no mic icon", item.Description);
    }
}
