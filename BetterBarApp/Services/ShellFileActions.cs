using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace BetterBarApp.Services;

/// <summary>
/// Standard file actions invoked from the launcher's themed context menu — implemented directly
/// (clipboard / recycle / shell verbs) so the menu can stay a WPF menu instead of the native shell
/// menu, while still doing what Explorer would.
/// </summary>
public static class ShellFileActions
{
    /// <summary>Opens Explorer with the file selected.</summary>
    public static void OpenLocation(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>Puts the file on the clipboard so it can be pasted in Explorer (copy, not move).</summary>
    public static void CopyToClipboard(string path)
    {
        try { Clipboard.SetFileDropList(new StringCollection { path }); }
        catch { }
    }

    /// <summary>Sends the file to the Recycle Bin (with the shell's normal confirmation).
    /// Returns true only if it was actually deleted.</summary>
    public static bool Delete(string path)
    {
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_DELETE,
                pFrom  = path + "\0\0",          // double-null terminated list
                fFlags = FOF_ALLOWUNDO,          // → Recycle Bin
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch { return false; }
    }

    /// <summary>Opens the file's shell Properties dialog.</summary>
    public static void ShowProperties(string path)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask  = SEE_MASK_INVOKEIDLIST,
                lpVerb = "properties",
                lpFile = path,
                nShow  = SW_SHOW,
            };
            ShellExecuteEx(ref info);
        }
        catch { }
    }

    // ── interop ──────────────────────────────────────────────────────────────────
    private const uint   FO_DELETE       = 0x0003;
    private const ushort FOF_ALLOWUNDO   = 0x0040;
    private const uint   SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int    SW_SHOW         = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint   wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string  pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int    cbSize;
        public uint   fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int    nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint   dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
