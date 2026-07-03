using System.IO;
using System.Runtime.InteropServices;

namespace BetterBarApp.Services.Search;

/// <summary>
/// Live filesystem browsing for the start-menu search box. When the typed text reads as
/// a path (a drive like <c>C:\</c>, or a UNC path past the server like <c>\\srv\</c>),
/// the menu lists the files/folders at that location instead of running the normal index,
/// filtered by whatever follows the last backslash. e.g. <c>\\srv\notes</c> lists the
/// items in the root of <c>\\srv</c> whose name contains "notes".
/// </summary>
public static class PathSearch
{
    /// <summary>
    /// True if <paramref name="text"/> should be treated as a path. <paramref name="dir"/>
    /// is the folder to enumerate (everything up to and including the last backslash) and
    /// <paramref name="filter"/> is the partial name after it (may be empty).
    /// </summary>
    public static bool TryParse(string text, out string dir, out string filter)
    {
        dir = filter = "";
        if (string.IsNullOrEmpty(text)) return false;

        // Drive path: "X:\..."  — triggered by the backslash after the drive letter.
        bool drive = text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && text[2] == '\\';

        // UNC path: "\\server\..." — triggered by the backslash AFTER the server name.
        bool unc = text.StartsWith(@"\\", StringComparison.Ordinal)
                   && text.Length > 2
                   && text.IndexOf('\\', 2) >= 0;

        if (!drive && !unc) return false;

        int lastSlash = text.LastIndexOf('\\');
        if (lastSlash < 0) return false;
        dir    = text[..(lastSlash + 1)];   // include the trailing backslash
        filter = text[(lastSlash + 1)..];
        return true;
    }

    /// <summary>Enumerates folders (first) then files at <paramref name="dir"/> whose name
    /// contains <paramref name="filter"/>, up to <paramref name="limit"/>. The root of a
    /// server (<c>\\srv\</c>) lists its shares. Returns empty on any error.</summary>
    public static IReadOnlyList<SearchEntry> Enumerate(string dir, string filter, int limit, CancellationToken ct)
    {
        var results = new List<SearchEntry>();
        if (limit <= 0) return results;

        bool Match(string name) =>
            filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        void Add(string full, string name, EntryKind kind) =>
            results.Add(SearchEntry.Create(full, name, "path", kind, full));

        // \\server\  → list the server's shares.
        if (IsServerRoot(dir, out var server))
        {
            foreach (var share in EnumerateShares(server))
            {
                if (ct.IsCancellationRequested) break;
                if (!Match(share)) continue;
                Add(dir + share, share, EntryKind.Folder);
                if (results.Count >= limit) break;
            }
            return results;
        }

        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                if (ct.IsCancellationRequested || results.Count >= limit) return results;
                var name = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar));
                if (Match(name)) Add(d, name, EntryKind.Folder);
            }
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (ct.IsCancellationRequested || results.Count >= limit) return results;
                var name = Path.GetFileName(f);
                if (Match(name)) Add(f, name, EntryKind.Document);
            }
        }
        catch { /* path being typed / inaccessible → return what we have */ }

        return results;
    }

    // dir is "\\server\" with no share segment after it.
    private static bool IsServerRoot(string dir, out string server)
    {
        server = "";
        if (!dir.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var rest = dir[2..].TrimEnd('\\');
        if (rest.Length == 0 || rest.Contains('\\')) return false;   // has a share → real path
        server = rest;
        return true;
    }

    // ── Share enumeration (NetShareEnum, level 1) ────────────────────────────────
    private static IEnumerable<string> EnumerateShares(string server)
    {
        var shares = new List<string>();
        int resume = 0;
        int rc = NetShareEnum(@"\\" + server, 1, out IntPtr buf, MAX_PREFERRED_LENGTH,
                              out int read, out _, ref resume);
        if (rc != 0 || buf == IntPtr.Zero) return shares;   // 0 = NERR_Success
        try
        {
            int size = Marshal.SizeOf<SHARE_INFO_1>();
            for (int i = 0; i < read; i++)
            {
                var info = Marshal.PtrToStructure<SHARE_INFO_1>(buf + i * size);
                bool special = (info.type & STYPE_SPECIAL) != 0;             // IPC$, ADMIN$, C$…
                bool disk    = (info.type & 0x00FFFFFF) == STYPE_DISKTREE;   // a normal folder share
                if (disk && !special && !info.netname.EndsWith("$", StringComparison.Ordinal))
                    shares.Add(info.netname);
            }
        }
        finally { NetApiBufferFree(buf); }
        return shares;
    }

    private const uint STYPE_DISKTREE = 0;
    private const uint STYPE_SPECIAL  = 0x80000000;
    private const int  MAX_PREFERRED_LENGTH = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHARE_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string netname;
        public uint type;
        [MarshalAs(UnmanagedType.LPWStr)] public string remark;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string servername, int level, out IntPtr bufptr,
        int prefmaxlen, out int entriesread, out int totalentries, ref int resumeHandle);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
