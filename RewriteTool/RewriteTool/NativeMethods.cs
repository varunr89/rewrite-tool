using System.Runtime.InteropServices;

namespace RewriteTool;

internal static class NativeMethods
{
    // --- Hotkey ---
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;

    // --- Focus ---
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // --- Keyboard state ---
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int VK_MENU = 0x12; // Alt

    // --- Clipboard ---
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // --- SendInput ---
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const ushort VK_C = 0x43;
    public const ushort VK_V = 0x56;
    public const ushort VK_LCONTROL = 0xA2;

    /// <summary>Send a key chord (e.g. Ctrl+C). Presses modifier, presses key, releases key, releases modifier.</summary>
    public static void SendKeyChord(ushort modifier, ushort key)
    {
        var inputs = new INPUT[4];
        int size = Marshal.SizeOf<INPUT>();

        inputs[0].Type = INPUT_KEYBOARD;
        inputs[0].Union.Keyboard.VirtualKey = modifier;

        inputs[1].Type = INPUT_KEYBOARD;
        inputs[1].Union.Keyboard.VirtualKey = key;

        inputs[2].Type = INPUT_KEYBOARD;
        inputs[2].Union.Keyboard.VirtualKey = key;
        inputs[2].Union.Keyboard.Flags = KEYEVENTF_KEYUP;

        inputs[3].Type = INPUT_KEYBOARD;
        inputs[3].Union.Keyboard.VirtualKey = modifier;
        inputs[3].Union.Keyboard.Flags = KEYEVENTF_KEYUP;

        SendInput((uint)inputs.Length, inputs, size);
    }
}
