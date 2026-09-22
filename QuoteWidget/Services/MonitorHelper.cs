using System.Runtime.InteropServices;

namespace QuoteWidget.Services;

/// <summary>
/// 多显示器工具：以显示器设备名（如 \\.\DISPLAY1）标识每一块屏幕，
/// 支撑"按屏记忆挂件位置"——换扩展屏/拔掉显示器时位置不会错乱。
/// </summary>
public static class MonitorHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumProc proc, IntPtr data);

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>物理坐标点所在显示器的设备名；失败返回 null。</summary>
    public static string? DeviceNameAt(int physicalX, int physicalY)
    {
        try
        {
            var point = new POINT { X = physicalX, Y = physicalY };
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return null;
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前所有显示器设备名。</summary>
    public static List<string> AllDeviceNames()
    {
        var list = new List<string>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
                if (GetMonitorInfo(monitor, ref info)) list.Add(info.szDevice);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }
}
