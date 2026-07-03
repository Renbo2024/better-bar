using System.Runtime.InteropServices;
using System.Windows;

namespace BetterBarApp.Services;

/// <summary>
/// A low-level keyboard hook that turns a solo tap of the Windows key into an action (open our Start
/// Button menu) while leaving Win+key shortcuts working.
///
/// The OS opens its own Start menu on a solo Win down+up. To take that over without breaking shortcuts
/// we SWALLOW the real Win key entirely, track it ourselves, and:
///   • solo tap (Win down then up, nothing in between) → fire the action; the OS never saw Win, so no
///     Start menu and no stuck key;
///   • Win+other → synthesise a Win-down (batched ahead of that first key, so ordering holds) so the
///     shortcut registers, then synthesise the Win-up on release.
/// Injected events carry LLKHF_INJECTED, so the hook ignores them and lets them reach the OS.
/// </summary>
public static class WinKeyHook
{
    private static IntPtr _hook;
    private static HookProc? _proc;     // kept alive for the unmanaged callback
    private static Action? _onTap;

    private static bool _winDown, _combo, _winInjected;
    private static int  _winVk;

    public static void Install(Action onTap)
    {
        if (_hook != IntPtr.Zero) return;
        _onTap = onTap;
        _proc  = Callback;
        _hook  = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((info.flags & LLKHF_INJECTED) == 0)   // our own synthesised events pass straight through
                {
                    var result = Process(info, (int)wParam);
                    if (result != IntPtr.Zero) return result;   // (IntPtr)1 = swallow
                }
            }
            catch { /* never let a hook failure wedge the keyboard */ }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static IntPtr Process(KBDLLHOOKSTRUCT info, int msg)
    {
        int  vk    = (int)info.vkCode;
        bool down  = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool up    = msg == WM_KEYUP   || msg == WM_SYSKEYUP;
        bool isWin = vk == VK_LWIN || vk == VK_RWIN;

        if (isWin)
        {
            if (down)
            {
                if (!_winDown) { _winDown = true; _combo = false; _winInjected = false; _winVk = vk; }
                return 1;   // swallow the real Win down; the OS must not see it yet
            }
            if (up)
            {
                bool wasDown = _winDown, combo = _combo, injected = _winInjected;
                _winDown = false; _combo = false; _winInjected = false;
                if (injected) SendKey(_winVk, keyUp: true);   // release the Win we synthesised for a shortcut
                else if (wasDown && !combo) Fire();           // solo tap → our menu
                return 1;
            }
            return IntPtr.Zero;
        }

        // A non-Win key while Win is held → it's a shortcut, not a solo tap.
        if (_winDown && down)
        {
            if (!_winInjected)
            {
                // Synthesise Win-down + this key together so Win is guaranteed to precede the key.
                SendComboStart(_winVk, vk, (info.flags & LLKHF_EXTENDED) != 0);
                _winInjected = true; _combo = true;
                return 1;   // we re-injected this key; swallow the real one
            }
            _combo = true;  // Win already synthesised-down; let further keys pass through
        }
        return IntPtr.Zero;
    }

    private static void Fire()
    {
        var disp = Application.Current?.Dispatcher;
        // Post: the hook must return promptly, and opening a window must run on the UI thread.
        if (disp != null) disp.BeginInvoke(() => { try { _onTap?.Invoke(); } catch { } });
    }

    // ── Synthesised input ──────────────────────────────────────────────────────────
    private static void SendComboStart(int winVk, int keyVk, bool extended)
    {
        var inputs = new[] { KeyInput((ushort)winVk, false, false), KeyInput((ushort)keyVk, false, extended) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(int vk, bool keyUp)
    {
        var inputs = new[] { KeyInput((ushort)vk, keyUp, false) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort vk, bool keyUp, bool extended)
    {
        uint flags = 0;
        if (keyUp)    flags |= KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } },
        };
    }

    // ── Win32 ──────────────────────────────────────────────────────────────────────
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int  VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    private const int  INPUT_KEYBOARD = 1;
}
