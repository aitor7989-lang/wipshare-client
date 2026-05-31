using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using WinRT;

namespace WipShare.Client.Native;

/// <summary>
/// Win32 / COM interop surface for WipShare. Foreground-window helpers live here; the
/// GraphicsCaptureItemInterop COM interface is added in the capture step.
/// </summary>
internal static class Win32Interop
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ---- Window extended styles (for click-through during recording) -------

    public const int GWL_EXSTYLE        = -20;
    public const int WS_EX_TRANSPARENT  = 0x00000020;
    public const int WS_EX_LAYERED      = 0x00080000;
    public const int WS_EX_TOOLWINDOW   = 0x00000080;
    public const int WS_EX_NOACTIVATE   = 0x08000000;

    // x64 binaries always use the *Ptr variants.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // ---- Physical-pixel window placement (DPI-robust pill positioning) -----

    public const int SWP_NOSIZE     = 0x0001;
    public const int SWP_NOMOVE     = 0x0002;
    public const int SWP_NOZORDER   = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // ---- Capture-exclusion (the overlay shouldn't film itself) ------------

    public const uint WDA_NONE               = 0x00000000;
    public const uint WDA_MONITOR            = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Win10 2004+

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // ---- Monitor + DPI ------------------------------------------------------

    public const uint MONITOR_DEFAULTTONULL    = 0x00000000;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    public enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI   = 1,
        MDT_RAW_DPI       = 2,
        MDT_DEFAULT       = MDT_EFFECTIVE_DPI,
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll", ExactSpelling = true)]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        public static MONITORINFO New() => new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(512);
        var n = GetWindowTextW(hWnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        var n = GetClassNameW(hWnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }

    // ---- WinRT activation + GraphicsCaptureItem interop -------------------

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsDeleteString(IntPtr hstring);

    private static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    /// <summary>
    /// Creates a <see cref="GraphicsCaptureItem"/> wrapping the supplied window
    /// via the IGraphicsCaptureItemInterop static factory.
    /// </summary>
    public static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("HWND is null", nameof(hwnd));
        var interop = GetCaptureItemInterop(out IntPtr classId);
        try
        {
            Guid itemIid = IID_IGraphicsCaptureItem;
            int hr = interop.CreateForWindow(hwnd, ref itemIid, out IntPtr pItem);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(pItem); }
            finally { Marshal.Release(pItem); }
        }
        finally { WindowsDeleteString(classId); }
    }

    public static GraphicsCaptureItem CreateCaptureItemForMonitor(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) throw new ArgumentException("HMONITOR is null", nameof(hMonitor));
        var interop = GetCaptureItemInterop(out IntPtr classId);
        try
        {
            Guid itemIid = IID_IGraphicsCaptureItem;
            int hr = interop.CreateForMonitor(hMonitor, ref itemIid, out IntPtr pItem);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(pItem); }
            finally { Marshal.Release(pItem); }
        }
        finally { WindowsDeleteString(classId); }
    }

    private static IGraphicsCaptureItemInterop GetCaptureItemInterop(out IntPtr classId)
    {
        const string ClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(ClassName, (uint)ClassName.Length, out classId);
        Guid iidInterop = typeof(IGraphicsCaptureItemInterop).GUID;
        RoGetActivationFactory(classId, ref iidInterop, out object factory);
        return (IGraphicsCaptureItemInterop)factory;
    }
}

/// <summary>
/// Static factory interface for GraphicsCaptureItem — bridges raw HWND/HMONITOR
/// handles into the projected WinRT type. Defined in Windows.Graphics.Capture.Interop.h.
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig] int CreateForWindow(IntPtr window, [In] ref Guid iid, out IntPtr result);
    [PreserveSig] int CreateForMonitor(IntPtr monitor, [In] ref Guid iid, out IntPtr result);
}
