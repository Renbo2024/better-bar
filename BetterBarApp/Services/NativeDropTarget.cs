using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace BetterBarApp.Services;

/// <summary>
/// Direct Win32/OLE drag-and-drop registration that bypasses WPF's AllowDrop
/// system. Used by the panel window after every WPF approach to accept external
/// drops failed (red "no-drop" cursor persisted regardless of AllowDrop+handler
/// permutations). This is the same low-level path Explorer uses internally.
/// </summary>
public static class NativeDropTarget
{
    [DllImport("ole32.dll")]
    public static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);

    [DllImport("ole32.dll")]
    public static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr pvReserved);

    // ── UIPI bypass for drag-drop from lower-integrity processes ──────────────
    // If BetterBar runs at a higher integrity level than Explorer (common when
    // launched from an elevated dev environment, or "Run as administrator"),
    // Windows silently blocks drag-drop messages from Explorer to our window.
    // ChangeWindowMessageFilterEx with MSGFLT_ALLOW whitelists specific messages
    // so OLE drag-drop from Explorer reaches our HWND regardless of IL difference.

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ChangeWindowMessageFilterEx(
        IntPtr hwnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    public const uint MSGFLT_ALLOW       = 1;
    public const uint WM_DROPFILES       = 0x0233;
    public const uint WM_COPYDATA        = 0x004A;
    public const uint WM_COPYGLOBALDATA  = 0x0049;

    public static void AllowDragDropMessages(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL { public int x; public int y; }

    public const int DROPEFFECT_NONE = 0;
    public const int DROPEFFECT_COPY = 1;
    public const int DROPEFFECT_MOVE = 2;
    public const int DROPEFFECT_LINK = 4;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000122-0000-0000-C000-000000000046")]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter(IDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect);
        [PreserveSig] int DragOver(int grfKeyState, POINTL pt, ref int pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(IDataObject pDataObj, int grfKeyState, POINTL pt, ref int pdwEffect);
    }
}

/// <summary>
/// Concrete IDropTarget implementation. Forwards all four method calls into
/// caller-supplied delegates so the receiving window doesn't have to implement
/// the COM interface itself.
/// </summary>
[ClassInterface(ClassInterfaceType.None)]
[ComVisible(true)]
public class PanelDropTarget : NativeDropTarget.IDropTarget
{
    public Action<NativeDropTarget.POINTL>? OnDragMove;
    public Action?                          OnDragLeave;
    public Action<NativeDropTarget.POINTL>? OnDrop;

    public int DragEnter(IDataObject pDataObj, int grfKeyState, NativeDropTarget.POINTL pt, ref int pdwEffect)
    {
        pdwEffect = NativeDropTarget.DROPEFFECT_COPY;
        OnDragMove?.Invoke(pt);
        return 0;   // S_OK
    }

    public int DragOver(int grfKeyState, NativeDropTarget.POINTL pt, ref int pdwEffect)
    {
        pdwEffect = NativeDropTarget.DROPEFFECT_COPY;
        OnDragMove?.Invoke(pt);
        return 0;
    }

    public int DragLeave()
    {
        OnDragLeave?.Invoke();
        return 0;
    }

    public int Drop(IDataObject pDataObj, int grfKeyState, NativeDropTarget.POINTL pt, ref int pdwEffect)
    {
        pdwEffect = NativeDropTarget.DROPEFFECT_COPY;
        OnDrop?.Invoke(pt);
        return 0;
    }
}
