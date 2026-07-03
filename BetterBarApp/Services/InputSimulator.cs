using System.Runtime.InteropServices;

namespace BetterBarApp.Services;

/// <summary>Synthesises keystrokes via SendInput (e.g. to open a system flyout).</summary>
public static class InputSimulator
{
    /// <summary>Taps Win + <paramref name="vk"/> (Win down, key down/up, Win up).</summary>
    public static void WinKeyChord(ushort vk)
    {
        var inputs = new[]
        {
            Key(VK_LWIN, up: false),
            Key(vk,      up: false),
            Key(vk,      up: true),
            Key(VK_LWIN, up: true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0u } },
    };

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
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int  INPUT_KEYBOARD   = 1;
    private const uint KEYEVENTF_KEYUP  = 0x0002;
    private const ushort VK_LWIN        = 0x5B;
}
