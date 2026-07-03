using System.Runtime.InteropServices;

namespace BetterBarApp.Services;

/// <summary>
/// Power-state queries and actions (shut down / restart / sleep / hibernate / log off), implemented
/// with the Win32 APIs directly so there's no console-window flash from shelling out to shutdown.exe.
/// Shut down and restart enable the required <c>SeShutdownPrivilege</c> on the process token first.
/// </summary>
public static class PowerActions
{
    /// <summary>True if hibernation is enabled on this machine.</summary>
    public static bool HibernateAvailable
    {
        get { try { return IsPwrHibernateAllowed(); } catch { return false; } }
    }

    public static void Shutdown() => ExitWindows(EWX_POWEROFF | EWX_FORCEIFHUNG, requiresPrivilege: true);
    public static void Reboot()   => ExitWindows(EWX_REBOOT   | EWX_FORCEIFHUNG, requiresPrivilege: true);
    public static void LogOff()   => ExitWindows(EWX_LOGOFF   | EWX_FORCEIFHUNG, requiresPrivilege: false);

    public static void Sleep()     => Suspend(hibernate: false);
    public static void Hibernate() => Suspend(hibernate: true);

    // ── Implementation ───────────────────────────────────────────────────────────────────────────
    private static void ExitWindows(uint flags, bool requiresPrivilege)
    {
        try
        {
            if (requiresPrivilege) EnableShutdownPrivilege();
            ExitWindowsEx(flags, SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED);
        }
        catch { /* never take the bar down because a power action failed */ }
    }

    private static void Suspend(bool hibernate)
    {
        try { SetSuspendState(hibernate, false, false); }
        catch { }
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out LUID luid)) return;
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid          = luid,
                Attributes    = SE_PRIVILEGE_ENABLED,
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    // ── Win32 ────────────────────────────────────────────────────────────────────────────────────
    private const uint EWX_LOGOFF       = 0x00000000;
    private const uint EWX_REBOOT       = 0x00000002;
    private const uint EWX_POWEROFF     = 0x00000008;
    private const uint EWX_FORCEIFHUNG  = 0x00000010;

    private const uint SHTDN_REASON_MAJOR_OTHER  = 0x00000000;
    private const uint SHTDN_REASON_MINOR_OTHER  = 0x00000000;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY             = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED    = 0x0002;
    private const string SE_SHUTDOWN_NAME      = "SeShutdownPrivilege";

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern bool IsPwrHibernateAllowed();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
}
