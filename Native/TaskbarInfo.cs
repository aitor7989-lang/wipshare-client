using System.Runtime.InteropServices;
using static WipShare.Client.Native.Win32Interop;

namespace WipShare.Client.Native;

/// <summary>
/// The placement target for the toast stack: the work area (in physical pixels)
/// of the monitor that hosts the tray, plus extra clearance when the taskbar is
/// auto-hidden. We anchor to the work area rather than assuming a 48px bottom bar
/// — that already accounts for the taskbar's real edge, size, and DPI.
/// </summary>
internal readonly record struct ToastAnchor(RECT WorkArea, int AutoHideClearance);

/// <summary>
/// Thin wrapper over SHAppBarMessage to learn the taskbar's auto-hide state and
/// thickness, and over GetMonitorInfo for the work area. All values are physical
/// pixels so callers can place no-activate windows via SetWindowPos directly.
/// </summary>
internal static class TaskbarInfo
{
    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const int ABS_AUTOHIDE = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    /// <summary>
    /// Work area + auto-hide clearance for the primary monitor (a fine target for
    /// the tray-anchored stack). Falls back to a virtual full-screen rect if the
    /// monitor query fails.
    /// </summary>
    public static ToastAnchor GetPrimary()
    {
        RECT work;
        var hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
        var mi = MONITORINFO.New();
        if (hMon != IntPtr.Zero && GetMonitorInfoW(hMon, ref mi))
        {
            work = mi.rcWork;
        }
        else
        {
            // Last-ditch fallback: assume a 1080p primary with a 48px taskbar.
            work = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 - 48 };
        }

        int autoHideClearance = 0;
        try
        {
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            long state = SHAppBarMessage(ABM_GETSTATE, ref data).ToInt64();
            if ((state & ABS_AUTOHIDE) != 0)
            {
                // Auto-hidden bars leave the work area at full size; reserve the
                // bar's own thickness so toasts never sit under the hidden bar.
                var posData = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
                if (SHAppBarMessage(ABM_GETTASKBARPOS, ref posData) != IntPtr.Zero)
                {
                    int thickness = Math.Min(posData.rc.Height, posData.rc.Width);
                    autoHideClearance = thickness > 0 && thickness < 200 ? thickness : 48;
                }
                else
                {
                    autoHideClearance = 48;
                }
            }
        }
        catch
        {
            autoHideClearance = 0; // best-effort; a docked bar needs no clearance
        }

        return new ToastAnchor(work, autoHideClearance);
    }
}
