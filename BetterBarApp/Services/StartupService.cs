using Microsoft.Win32;

namespace BetterBarApp.Services;

/// <summary>
/// "Launch BetterBar when I sign in" for the current user, via the standard per-user Run key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). It points at the current executable, so it
/// keeps working wherever the app lives. When a proper installer arrives it can manage this same key
/// (or hand off to a Startup-folder / scheduled-task scheme) without changing this surface.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "BetterBar";

    /// <summary>The executable to relaunch at sign-in (the running host exe).</summary>
    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>True if a Run entry for BetterBar exists for the current user.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch { return false; }
        }
    }

    /// <summary>Add or remove the per-user sign-in entry. Failures are swallowed (never crash the bar).</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return;

            if (enabled)
            {
                var path = ExecutablePath;
                if (string.IsNullOrWhiteSpace(path)) return;
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* registry unavailable / locked — leave the toggle's stored intent only */ }
    }
}
