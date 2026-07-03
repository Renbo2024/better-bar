namespace BetterBarApp.Services;

public enum ThemeKeyKind { Color, CornerRadius }

/// <summary>One editable entry of a theme palette.</summary>
public sealed record ThemeKey(string Key, string Display, ThemeKeyKind Kind, string Group);

/// <summary>
/// The full set of palette keys a theme defines, in editor display order and grouped. This is the
/// single source of truth for what the theme editor exposes and what ThemeService reads/writes;
/// it mirrors the keys in Themes/Dark.xaml &amp; Light.xaml.
/// </summary>
public static class ThemeSchema
{
    public static IReadOnlyList<ThemeKey> Keys { get; } =
    [
        new("PanelBackground",        "Panel background",          ThemeKeyKind.Color,        "Taskbar"),
        new("TaskButtonCornerRadius", "Task button corner radius", ThemeKeyKind.CornerRadius, "Taskbar"),
        new("TaskBtnFg",              "Task button text",          ThemeKeyKind.Color,        "Taskbar"),
        new("TaskBtnHoverBg",         "Task button hover",         ThemeKeyKind.Color,        "Taskbar"),
        new("TaskBtnActiveBg",        "Task button active",        ThemeKeyKind.Color,        "Taskbar"),
        new("TaskBtnUnselectedAccent","Unselected accent (default)",ThemeKeyKind.Color,       "Taskbar"),
        new("PanelSeparator",         "Separator line",            ThemeKeyKind.Color,        "Taskbar"),

        new("Accent",      "Accent",            ThemeKeyKind.Color, "Accent"),
        new("AccentHover", "Accent (hover)",    ThemeKeyKind.Color, "Accent"),
        new("AccentPress", "Accent (pressed)",  ThemeKeyKind.Color, "Accent"),

        new("StartMenuBg",      "Start menu background",   ThemeKeyKind.Color, "Start menu"),
        new("StartMenuBorder",  "Start menu border",       ThemeKeyKind.Color, "Start menu"),
        new("StartMenuFieldBg", "Start menu search field", ThemeKeyKind.Color, "Start menu"),

        new("WindowBg",      "Dialog background", ThemeKeyKind.Color, "Dialogs"),
        new("SurfaceBg",     "Dialog surface",    ThemeKeyKind.Color, "Dialogs"),
        new("CtrlBorder",    "Control border",    ThemeKeyKind.Color, "Dialogs"),
        new("TextPrimary",   "Text (primary)",    ThemeKeyKind.Color, "Dialogs"),
        new("TextSecondary", "Text (secondary)",  ThemeKeyKind.Color, "Dialogs"),
        new("ItemHover",     "Item hover",        ThemeKeyKind.Color, "Dialogs"),
        new("ItemSelected",  "Item selected",     ThemeKeyKind.Color, "Dialogs"),
    ];

    public static ThemeKey? Find(string key)
    {
        foreach (var k in Keys) if (k.Key == key) return k;
        return null;
    }
}
