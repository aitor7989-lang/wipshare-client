using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using static WipShare.Client.Native.Win32Interop;

namespace WipShare.Client.Notifications;

/// <summary>Shared Win32 wiring for toast-family windows (the stack + overflow pill).</summary>
internal static class ToastInterop
{
    /// <summary>
    /// Makes a window no-activate (never steals focus), hidden from alt-tab, and
    /// excluded from screen capture. Call from the window's SourceInitialized.
    /// </summary>
    public static void MakeNoActivateCaptureExcluded(Window window, ILogger logger)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            logger.LogWarning("Toast pill WDA_EXCLUDEFROMCAPTURE failed: {Err}", Marshal.GetLastWin32Error());
    }
}
