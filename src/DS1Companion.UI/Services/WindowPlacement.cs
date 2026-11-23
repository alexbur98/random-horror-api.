using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DS1Companion.Core.Persistence;

namespace DS1Companion.UI.Services;

/// <summary>
/// Запоминание монитора, позиции и размера окна. Работает в физических пикселях через Win32,
/// поэтому корректно для мониторов с разным DPI (Per-Monitor V2).
/// При первом запуске выбирает вертикальный (портретный) монитор, если он есть.
/// </summary>
public static class WindowPlacement
{
    public sealed record MonitorInfo(string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary)
    {
        public bool IsPortrait => Bounds.Height > Bounds.Width;
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (Describe(h) is { } m) list.Add(m);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void Restore(Window window, AppSettings s)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitors = GetMonitors();
        if (monitors.Count == 0) return;

        RECT? target = null;
        if (s is { WindowLeft: { } l, WindowTop: { } t, WindowWidth: { } w, WindowHeight: { } h } && w > 100 && h > 100)
        {
            var saved = new RECT { Left = (int)l, Top = (int)t, Right = (int)(l + w), Bottom = (int)(t + h) };
            if (monitors.Any(m => Intersects(m.WorkArea, saved))) target = saved;
        }

        if (target is null)
        {
            var monitor = monitors.FirstOrDefault(m => m.DeviceName == s.MonitorDeviceName)
                          ?? monitors.FirstOrDefault(m => m.IsPortrait && !m.IsPrimary)
                          ?? monitors.FirstOrDefault(m => !m.IsPrimary)
                          ?? monitors.First(m => m.IsPrimary);
            var wa = monitor.WorkArea;
            var dpiScale = GetDpiForWindow(hwnd) / 96.0;
            var width = Math.Min(wa.Width, (int)(760 * dpiScale));
            target = new RECT { Left = wa.Left + (wa.Width - width) / 2, Top = wa.Top, Right = wa.Left + (wa.Width - width) / 2 + width, Bottom = wa.Bottom };
        }

        var r = target.Value;
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        if (s.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    public static void Save(Window window, AppSettings s)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        s.WindowMaximized = window.WindowState == WindowState.Maximized;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        RECT rect;
        if (s.WindowMaximized && GetWindowPlacement(hwnd, ref placement)) rect = placement.rcNormalPosition;
        else if (!GetWindowRect(hwnd, out rect)) return;
        if (window.WindowState == WindowState.Minimized && GetWindowPlacement(hwnd, ref placement)) rect = placement.rcNormalPosition;

        s.WindowLeft = rect.Left;
        s.WindowTop = rect.Top;
        s.WindowWidth = rect.Width;
        s.WindowHeight = rect.Height;
        s.MonitorDeviceName = Describe(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST))?.DeviceName;
    }

    private static bool Intersects(RECT a, RECT b) =>
        Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 80 && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 80;

    private static MonitorInfo? Describe(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return null;
        return new MonitorInfo(info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0);
    }

    // ── Win32 ──
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
