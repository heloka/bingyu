using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Bingyu;

// WindowStyle=None needs an explicit work-area bound when maximized; otherwise
// the web content and the native rail extend underneath the Windows taskbar.
internal static class WindowBounds
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;
    private const int DwMwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);

    public static void Attach(Window window)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }

    private static IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo || !TryGetMonitorRects(hwnd, out var monitor, out var work)) return IntPtr.Zero;
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition = new Point { X = work.Left - monitor.Left, Y = work.Top - monitor.Top };
        info.MaxSize = new Point { X = work.Width, Y = work.Height };
        Marshal.StructureToPtr(info, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private static bool TryGetMonitorRects(IntPtr hwnd, out Rect monitor, out Rect work)
    {
        var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info)) { monitor = work = default; return false; }
        monitor = info.Monitor; work = info.Work; return true;
    }

    internal static bool TryGetVisibleWindowAndWorkArea(Window window, out Rect visible, out Rect work)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!TryGetMonitorRects(hwnd, out _, out work)) { visible = default; return false; }
        // DWM excludes the invisible resize border from the on-screen bounds.
        if (DwmGetWindowAttribute(hwnd, DwMwaExtendedFrameBounds, out visible, Marshal.SizeOf<Rect>()) != 0)
            return GetWindowRect(hwnd, out visible);
        return true;
    }
}
