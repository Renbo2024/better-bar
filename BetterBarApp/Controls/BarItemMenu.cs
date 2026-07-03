using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Controls;

/// <summary>
/// Shared builders for the bar-item right-click menus (task buttons, launcher icons …) so every
/// bar item gets the SAME themed look (the <c>SystemContextMenu</c> / <c>SystemMenuItem</c> /
/// <c>SystemMenuSeparator</c> styles) and the SAME BetterBar commands at the top.
/// </summary>
internal static class BarItemMenu
{
    /// <summary>A themed, empty <see cref="ContextMenu"/> placed above its target.</summary>
    public static ContextMenu Create(FrameworkElement owner, UIElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.Top };
        if (owner.TryFindResource("SystemContextMenu") is Style s) menu.Style = s;
        return menu;
    }

    public static MenuItem MakeItem(FrameworkElement owner, string header, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        if (owner.TryFindResource("SystemMenuItem") is Style s) mi.Style = s;
        return mi;
    }

    public static Separator MakeSeparator(FrameworkElement owner)
    {
        var sep = new Separator();
        if (owner.TryFindResource("SystemMenuSeparator") is Style s) sep.Style = s;
        return sep;
    }

    /// <summary>A WPF-UI symbol icon for a menu item, tinted to the menu's text colour.</summary>
    public static Wpf.Ui.Controls.SymbolIcon SymbolGlyph(Wpf.Ui.Controls.SymbolRegular symbol)
    {
        var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = symbol, FontSize = 15 };
        icon.SetResourceReference(Control.ForegroundProperty, "TaskBtnFg");
        return icon;
    }

    /// <summary>Appends a plain command item with a click handler.</summary>
    public static MenuItem AddCommand(FrameworkElement owner, ContextMenu menu, string header, Action onClick)
    {
        var mi = MakeItem(owner, header);
        mi.Click += (_, _) => onClick();
        menu.Items.Add(mi);
        return mi;
    }

    /// <summary>
    /// A "Configure &lt;item type&gt;" command that opens Settings straight to <paramref name="item"/>'s
    /// page within its <paramref name="def"/> bar definition. Returned (not added) so callers can place
    /// it where they like; tracked for removal in the shared panel menu.
    /// </summary>
    public static MenuItem MakeConfigureItem(FrameworkElement owner, BarDefinition def, BarItem item)
    {
        string typeName = ItemTypeRegistry.Types.FirstOrDefault(t => t.Key == item.TypeKey)?.DisplayName ?? "Item";
        var mi = MakeItem(owner, $"Configure {typeName}");
        mi.Icon = SymbolGlyph(Wpf.Ui.Controls.SymbolRegular.Settings24);
        mi.Click += (_, _) => owner.Dispatcher.BeginInvoke(() => SettingsWindow.ShowItem(def, item));
        return mi;
    }

    /// <summary>Adds BetterBar's own items (Settings, Exit) — placed at the TOP of every bar-item menu.</summary>
    public static void AddBetterBarCommands(FrameworkElement owner, ContextMenu menu)
    {
        var settings = MakeItem(owner, "BetterBar Settings");
        settings.Icon = AppImages.NewIcon();
        settings.Click += (_, _) => owner.Dispatcher.BeginInvoke(SettingsWindow.ShowOrActivate);
        menu.Items.Add(settings);

        var exit = MakeItem(owner, "Exit BetterBar");
        exit.Icon = SymbolGlyph(Wpf.Ui.Controls.SymbolRegular.Power24);
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);
    }
}
