using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterBarApp.Services.Search;

/// <summary>Semantic type of a result — drives the glyph and grouping.</summary>
public enum EntryKind
{
    DesktopApp, StoreApp, SettingModern, SettingClassic, Document, ExecutableFile, UserLocationItem, QuickLaunch, Folder,
}

/// <summary>How a result's <see cref="SearchEntry.LaunchTarget"/> is activated.</summary>
public enum LaunchKind
{
    /// <summary>Process.Start with UseShellExecute (file path, ms-settings: URI, .cpl/.msc).</summary>
    ShellExecute,
    /// <summary>A shell namespace item (AppsFolder app, Control Panel applet) launched by
    /// parsing the name to a PIDL and invoking ShellExecuteEx — works for non-file items.</summary>
    ShellItem,
}

/// <summary>
/// One launchable thing. Immutable; all match-relevant fields are precomputed at
/// index time so per-keystroke scoring does no heavy work (spec §3.1).
/// </summary>
public sealed record SearchEntry(
    string     Id,
    string     DisplayName,
    string     NormalizedName,
    string[]   Tokens,
    string     SourceId,
    EntryKind  Kind,
    string     LaunchTarget,     // shell-executable string or shell parsing name (see LaunchVia)
    LaunchKind LaunchVia,
    string[]   Keywords)         // extra match aliases (normalized); used for Settings
{
    public static SearchEntry Create(string id, string display, string sourceId, EntryKind kind,
                                      string launchTarget, IEnumerable<string>? keywords = null,
                                      LaunchKind launchVia = LaunchKind.ShellExecute)
    {
        var norm = SearchText.Normalize(display);
        var kw   = (keywords ?? []).Select(SearchText.Normalize).Where(k => k.Length > 0).ToArray();
        return new SearchEntry(id, display, norm, SearchText.Tokenize(norm), sourceId, kind, launchTarget, launchVia, kw);
    }

    public bool Equals(SearchEntry? other) => other != null && Id == other.Id && SourceId == other.SourceId;
    public override int GetHashCode() => HashCode.Combine(Id, SourceId);
}

/// <summary>A source's results for one query — becomes a UI section.</summary>
public sealed record SearchSection(string SourceId, string DisplayName, IReadOnlyList<SearchEntry> Items);

/// <summary>One provider of searchable entries (spec §3.3, trimmed for v1).</summary>
public interface ISearchSource
{
    string SourceId   { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct);

    /// <summary>Directories whose changes should re-enumerate JUST this source
    /// (per-source change pipeline). Empty for sources that aren't file-backed.</summary>
    IReadOnlyList<string> WatchRoots { get; }

    /// <summary>False when the source's backing store is currently unreachable — e.g. a
    /// configured folder whose drive is unplugged or whose share is offline. Such a source
    /// is skipped (contributes no results) without throwing, and the watcher's availability
    /// poll re-enumerates it once it returns. Always true for sources that are always
    /// reachable (the shell-backed apps / settings / quick-launch sources).</summary>
    bool IsAvailable => true;
}

/// <summary>Text normalization + tokenization shared by indexing and querying (spec §8.1).</summary>
public static class SearchText
{
    /// <summary>Lowercase, strip diacritics (FormD + drop non-spacing marks), trim.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var d  = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Split a normalized string on non-alphanumerics and letter/digit boundaries.</summary>
    public static string[] Tokenize(string normalized)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < normalized.Length)
        {
            if (!char.IsLetterOrDigit(normalized[i])) { i++; continue; }
            int start  = i;
            bool digit = char.IsDigit(normalized[i]);
            while (i < normalized.Length && char.IsLetterOrDigit(normalized[i]) && char.IsDigit(normalized[i]) == digit)
                i++;
            tokens.Add(normalized[start..i]);
        }
        return tokens.ToArray();
    }
}

/// <summary>Category glyphs (spec §6). Emoji render without a special icon font.</summary>
public static class EntryGlyph
{
    public static string For(EntryKind kind) => kind switch
    {
        EntryKind.DesktopApp     => "▣",
        EntryKind.StoreApp       => "🛍",
        EntryKind.SettingModern  => "⚙",
        EntryKind.SettingClassic => "🛠",
        EntryKind.Document       => "📄",
        EntryKind.ExecutableFile => "⚠",
        EntryKind.QuickLaunch    => "★",
        EntryKind.Folder         => "📁",
        _                        => "▣",
    };
}

/// <summary>Launches a result (spec §5). Throws on failure for the UI to surface.</summary>
public static class Launcher
{
    public static void Launch(SearchEntry entry)
    {
        if (entry.LaunchVia == LaunchKind.ShellItem)
            LaunchShellItem(entry.LaunchTarget);
        else
            Process.Start(new ProcessStartInfo(entry.LaunchTarget) { UseShellExecute = true });
    }

    // Reconstruct a captured PIDL (base64 bytes) and invoke it (apps, Control Panel
    // applets, "All Tasks" items) — works even for items with no re-parseable name.
    private static void LaunchShellItem(string base64Pidl)
    {
        var bytes = Convert.FromBase64String(base64Pidl);
        var pidl  = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, pidl, bytes.Length);
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize   = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask    = SEE_MASK_INVOKEIDLIST,
                lpIDList = pidl,
                nShow    = SW_SHOWNORMAL,
            };
            if (!ShellExecuteEx(ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;   // implies SEE_MASK_IDLIST
    private const int  SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
