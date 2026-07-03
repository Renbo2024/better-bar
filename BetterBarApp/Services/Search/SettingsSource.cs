namespace BetterBarApp.Services.Search;

/// <summary>
/// Settings source (spec §4.2): a curated, shipped dataset. Modern pages launch via
/// ms-settings: URIs; classic ones via shell-executable .cpl/.msc/.exe. Each carries
/// keyword aliases so functional searches ("wifi", "uninstall") hit the right page.
/// </summary>
public sealed class SettingsSource : ISearchSource
{
    public string SourceId    => "settings";
    public string DisplayName => "Settings";

    // Control Panel / settings rarely change at runtime → not watched (manual reload).
    public IReadOnlyList<string> WatchRoots { get; } = Array.Empty<string>();

    private sealed record Def(string Name, EntryKind Kind, string Target, params string[] Keys);

    private static readonly Def[] Data =
    {
        // ── Modern (ms-settings:) ────────────────────────────────────────────────
        new("Settings",              EntryKind.SettingModern, "ms-settings:",                       "settings"),
        new("Bluetooth & devices",   EntryKind.SettingModern, "ms-settings:bluetooth",              "bluetooth", "devices"),
        new("Wi-Fi",                 EntryKind.SettingModern, "ms-settings:network-wifi",           "wifi", "wireless", "network"),
        new("Network & internet",    EntryKind.SettingModern, "ms-settings:network-status",         "network", "internet", "ethernet"),
        new("Display",               EntryKind.SettingModern, "ms-settings:display",                "display", "screen", "resolution", "monitor", "scaling"),
        new("Sound",                 EntryKind.SettingModern, "ms-settings:sound",                  "sound", "audio", "volume", "speakers", "microphone"),
        new("Notifications",         EntryKind.SettingModern, "ms-settings:notifications",          "notifications"),
        new("Power & battery",       EntryKind.SettingModern, "ms-settings:powersleep",             "power", "battery", "sleep"),
        new("Storage",               EntryKind.SettingModern, "ms-settings:storagesense",           "storage", "disk space"),
        new("Apps & features",       EntryKind.SettingModern, "ms-settings:appsfeatures",           "apps", "uninstall", "programs", "features", "installed apps", "installed programs", "add or remove programs", "add remove programs", "programs and features"),
        new("Default apps",          EntryKind.SettingModern, "ms-settings:defaultapps",            "default apps", "associations"),
        new("Background",            EntryKind.SettingModern, "ms-settings:personalization-background", "wallpaper", "background", "desktop"),
        new("Colors",                EntryKind.SettingModern, "ms-settings:colors",                 "colors", "theme", "dark mode", "accent"),
        new("Themes",                EntryKind.SettingModern, "ms-settings:themes",                 "themes"),
        new("Lock screen",           EntryKind.SettingModern, "ms-settings:lockscreen",             "lock screen"),
        new("Taskbar",               EntryKind.SettingModern, "ms-settings:taskbar",                "taskbar"),
        new("Accounts",              EntryKind.SettingModern, "ms-settings:yourinfo",               "account", "user"),
        new("Sign-in options",       EntryKind.SettingModern, "ms-settings:signinoptions",          "password", "pin", "hello", "fingerprint", "sign in"),
        new("Date & time",           EntryKind.SettingModern, "ms-settings:dateandtime",            "date", "time", "clock", "timezone"),
        new("Language & region",     EntryKind.SettingModern, "ms-settings:regionlanguage",         "language", "region", "locale"),
        new("Windows Update",        EntryKind.SettingModern, "ms-settings:windowsupdate",          "update", "updates"),
        new("Privacy & security",    EntryKind.SettingModern, "ms-settings:privacy",                "privacy"),
        new("Printers & scanners",   EntryKind.SettingModern, "ms-settings:printers",               "printer", "scanner", "print"),
        new("Mouse",                 EntryKind.SettingModern, "ms-settings:mousetouchpad",          "mouse", "touchpad", "pointer"),
        new("Windows Security",      EntryKind.SettingModern, "ms-settings:windowsdefender",        "security", "antivirus", "defender", "firewall"),
        new("About",                 EntryKind.SettingModern, "ms-settings:about",                  "about", "pc info", "system", "specs"),
        new("Recovery",              EntryKind.SettingModern, "ms-settings:recovery",               "recovery", "reset"),
        new("Multitasking",          EntryKind.SettingModern, "ms-settings:multitasking",           "multitasking", "snap"),

        // ── Classic (.cpl / .msc / .exe — all shell-executable) ──────────────────
        new("Control Panel",         EntryKind.SettingClassic, "control.exe",   "control panel"),
        new("Device Manager",        EntryKind.SettingClassic, "devmgmt.msc",   "device manager", "hardware", "drivers"),
        new("Network Connections",   EntryKind.SettingClassic, "ncpa.cpl",      "network adapter", "connections", "ip"),
        new("System Properties",     EntryKind.SettingClassic, "sysdm.cpl",     "environment variables", "system properties", "computer name"),
        new("Disk Management",       EntryKind.SettingClassic, "diskmgmt.msc",  "disk management", "partition", "volumes"),
        new("Services",              EntryKind.SettingClassic, "services.msc",  "services"),
        new("Task Scheduler",        EntryKind.SettingClassic, "taskschd.msc",  "task scheduler", "scheduled tasks"),
        new("Event Viewer",          EntryKind.SettingClassic, "eventvwr.msc",  "event viewer", "logs"),
        new("Computer Management",   EntryKind.SettingClassic, "compmgmt.msc",  "computer management"),
    };

    public async Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct)
    {
        // Curated modern ms-settings: pages + classic admin tools (.msc/.cpl) that
        // aren't Control Panel applets.
        var entries = Data
            .Select(d => SearchEntry.Create($"settings:{d.Name}", d.Name, SourceId, d.Kind, d.Target, d.Keys))
            .ToList();
        var names = new HashSet<string>(entries.Select(e => e.DisplayName), StringComparer.OrdinalIgnoreCase);

        // Enumerated Control Panel applets (e.g. "Programs and Features"), deduped
        // against the curated names. These join the same Settings section.
        foreach (var cpl in await ShellNamespace.EnumerateControlPanelAsync(ct))
            if (names.Add(cpl.DisplayName))
                entries.Add(cpl);

        return entries;
    }
}
