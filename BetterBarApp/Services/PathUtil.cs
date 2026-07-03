using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterBarApp.Services;

public static class PathUtil
{
    /// <summary>
    /// Maps a mapped-network-drive path (e.g. "Z:\Sub") to its UNC form
    /// ("\\server\share\Sub"). Mapped drive letters aren't visible across security
    /// tokens (so an elevated process can't see them), but UNC paths are — so storing
    /// and reading the UNC makes such folders index reliably. Local drives and
    /// non-drive paths are returned unchanged.
    /// </summary>
    public static string ToUnc(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (root is not { Length: >= 2 } || root[1] != ':') return path;   // not a drive-letter path

            var sb  = new StringBuilder(1024);
            int len = sb.Capacity;
            if (WNetGetConnection(root[..2], sb, ref len) != 0) return path;    // not a mapped network drive

            var sub = path[root.Length..];
            return Path.Combine(sb.ToString(), sub);
        }
        catch { return path; }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}
