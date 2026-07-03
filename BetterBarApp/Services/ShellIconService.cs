using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BetterBarApp.Services;

public static class ShellIconService
{
    // ── IShellItemImageFactory ─────────────────────────────────────────────────
    // Preferred path: gets the icon at EXACTLY the requested pixel size
    // with no shortcut-arrow overlay (SIIGBF.IconOnly skips thumbnail compositing
    // and returns the raw shell icon assigned to the file type).

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [Flags]
    private enum SIIGBF { IconOnly = 0x4 }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateItemFromIDList(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    // ── SHGetFileInfo fallback ─────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr h);

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_LARGEICON   = 0x0000;
    private const int  ILD_NORMAL        = 0x0000;

    private static readonly Guid ShellItemImageFactoryGuid =
        new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    /// <summary>
    /// Returns a shell icon for <paramref name="path"/> sized to
    /// <paramref name="sizePixels"/> × <paramref name="sizePixels"/> physical pixels,
    /// with no shortcut-arrow overlay.
    /// Must be called from an STA thread.
    /// </summary>
    public static BitmapSource? GetIcon(string path, int sizePixels)
        => GetIconViaFactory(path, sizePixels)
        ?? GetIconViaImageList(path);

    /// <summary>
    /// Like <see cref="GetIcon"/> but for a shell item identified by captured PIDL
    /// bytes (apps, Control Panel applets, All-Tasks items) — used for entries whose
    /// parsing name isn't re-resolvable. Must be called from an STA thread.
    /// </summary>
    public static BitmapSource? GetIconFromIdList(byte[] pidlBytes, int sizePixels)
    {
        if (pidlBytes == null || pidlBytes.Length == 0) return null;
        var pidl = Marshal.AllocCoTaskMem(pidlBytes.Length);
        Marshal.Copy(pidlBytes, 0, pidl, pidlBytes.Length);
        try
        {
            SHCreateItemFromIDList(pidl, ShellItemImageFactoryGuid, out var factory);
            int hr = factory.GetImage(new SIZE { cx = sizePixels, cy = sizePixels }, SIIGBF.IconOnly, out IntPtr hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try   { return BitmapFromHBitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    // IShellItemImageFactory: exact requested size, no overlay.
    private static BitmapSource? GetIconViaFactory(string path, int sizePixels)
    {
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero,
                ShellItemImageFactoryGuid, out var factory);

            int hr = factory.GetImage(
                new SIZE { cx = sizePixels, cy = sizePixels },
                SIIGBF.IconOnly, out IntPtr hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;

            try   { return BitmapFromHBitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
    }

    // SHGetFileInfo + ImageList_GetIcon with ILD_NORMAL: no overlay, fixed 32 px.
    private static BitmapSource? GetIconViaImageList(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            var himl = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info),
                                     SHGFI_SYSICONINDEX | SHGFI_LARGEICON);
            if (himl == IntPtr.Zero) return null;

            var hIcon = ImageList_GetIcon(himl, info.iIcon, ILD_NORMAL);
            if (hIcon == IntPtr.Zero) return null;

            try   { return BitmapFromHIcon(hIcon); }
            finally { DestroyIcon(hIcon); }
        }
        catch { return null; }
    }

    private static BitmapSource? BitmapFromHBitmap(IntPtr hbm)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    private static BitmapSource? BitmapFromHIcon(IntPtr hIcon)
    {
        try
        {
            using var drawing = System.Drawing.Icon.FromHandle(hIcon);
            using var bmp     = drawing.ToBitmap();
            var hbmp          = bmp.GetHbitmap();
            try   { return BitmapFromHBitmap(hbmp); }
            finally { DeleteObject(hbmp); }
        }
        catch { return null; }
    }
}
