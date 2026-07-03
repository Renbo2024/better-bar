using System.Runtime.InteropServices;
using System.Threading;
using ManagedShell.ShellFolders;

namespace BetterBarApp.Services.Search;

/// <summary>
/// Enumerates shell namespace folders (AppsFolder, Control Panel applets, and the
/// Control Panel "All Tasks" list) the way Open Shell does — and captures each item's
/// PIDL bytes so it can be launched/iconed later by reconstructing the PIDL, even for
/// items whose parsing name isn't re-resolvable (e.g. "Add or remove programs").
/// Shell COM requires STA, so work runs on a dedicated STA thread.
/// </summary>
public static class ShellNamespace
{
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    // Control Panel "All Tasks" (a.k.a. GodMode) — the full granular task list.
    private const string AllTasksFolder = "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}";

    [DllImport("shell32.dll")] private static extern int  SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr ppsz);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);

    public static Task<IReadOnlyList<SearchEntry>> EnumerateAppsAsync(CancellationToken ct) =>
        RunStaAsync(EnumerateApps);

    public static Task<IReadOnlyList<SearchEntry>> EnumerateControlPanelAsync(CancellationToken ct) =>
        RunStaAsync(EnumerateControlPanel);

    private static IReadOnlyList<SearchEntry> EnumerateApps()
    {
        var list = new List<SearchEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnumFolder("shell:AppsFolder", (name, pidl) =>
        {
            if (!seen.Add(name)) return;
            var b64 = PidlToBase64(pidl);
            if (b64 == null) return;
            var kind = (ParsingName(pidl) ?? "").Contains('!') ? EntryKind.StoreApp : EntryKind.DesktopApp;
            list.Add(SearchEntry.Create(name, name, "apps", kind, b64, launchVia: LaunchKind.ShellItem));
        });
        return list;
    }

    private static IReadOnlyList<SearchEntry> EnumerateControlPanel()
    {
        var list = new List<SearchEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, IntPtr pidl)
        {
            if (!seen.Add(name)) return;
            var b64 = PidlToBase64(pidl);
            if (b64 != null)
                list.Add(SearchEntry.Create(name, name, "settings", EntryKind.SettingClassic, b64,
                                            launchVia: LaunchKind.ShellItem));
        }

        EnumFolder("shell:ControlPanelFolder", Add);   // the applets (Programs and Features, …)
        EnumFolder(AllTasksFolder, Add);                // granular tasks (Add or remove programs, …)
        return list;
    }

    private static void EnumFolder(string parsingName, Action<string, IntPtr> onItem)
    {
        ShellFolder? folder = null;
        try
        {
            folder = new ShellFolder(parsingName, IntPtr.Zero, false);
            foreach (var item in folder.Files.OfType<ShellItem>().ToList())
            {
                var name = item.DisplayName;
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("::{", StringComparison.Ordinal)) continue;
                if (item.AbsolutePidl == IntPtr.Zero) continue;
                onItem(name, item.AbsolutePidl);   // copies the PIDL now, while the item is alive
            }
        }
        catch { /* shell enumeration failure → whatever we have (source isolation) */ }
        finally { folder?.Dispose(); }
    }

    // A PIDL is a position-independent blob; copying its bytes lets us reconstruct and
    // invoke it later (the index holds it for the session).
    private static string? PidlToBase64(IntPtr pidl)
    {
        if (pidl == IntPtr.Zero) return null;
        uint size = ILGetSize(pidl);
        if (size == 0 || size > 64 * 1024) return null;
        var bytes = new byte[size];
        Marshal.Copy(pidl, bytes, 0, (int)size);
        return Convert.ToBase64String(bytes);
    }

    private static string? ParsingName(IntPtr pidl)
    {
        if (SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out var p) != 0) return null;
        try   { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static Task<IReadOnlyList<SearchEntry>> RunStaAsync(Func<IReadOnlyList<SearchEntry>> work)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<SearchEntry>>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
