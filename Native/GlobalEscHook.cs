using System.Runtime.InteropServices;

namespace WipShare.Client.Native;

/// <summary>
/// A low-level keyboard hook (WH_KEYBOARD_LL) that fires <see cref="Pressed"/>
/// when Esc is pressed, and swallows that Esc. Used only while a recording is in
/// flight so Esc can abort it without the pill having to steal focus from the
/// app being recorded. The callback runs on the installing (UI) thread.
/// </summary>
internal sealed class GlobalEscHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private bool _disposed;

    /// <summary>Raised on the UI thread when Esc is pressed.</summary>
    public event Action? Pressed;

    public GlobalEscHook()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is first
                if (vk == VK_ESCAPE)
                {
                    try { Pressed?.Invoke(); } catch { /* never let a handler crash the hook */ }
                    return (IntPtr)1; // swallow: Esc means "cancel recording"
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
