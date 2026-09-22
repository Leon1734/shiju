using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QuoteWidget.Services;

/// <summary>
/// 全屏应用检测（用于自动隐藏挂件）。
/// 经验教训：不能只依赖 SHQueryUserNotificationState 的 QUNS_BUSY——
/// 任务栏自动隐藏 + 最大化窗口等场景会被误判为"全屏"，导致挂件反复闪隐。
/// 因此采用行业通行的可靠判据：
///   ① 前台窗口矩形完整覆盖所在显示器（排除 1px 误差）
///   ② 且不是最大化状态（排除任务栏自动隐藏下的普通最大化窗口）
///   ③ 且不是桌面/任务栏/本程序
/// 另外保留 QUNS 的独占全屏(3)与演示模式(4)两个无歧义状态。
/// </summary>
public static class FullscreenWatcher
{
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();

    private const uint MonitorDefaultToNearest = 2;
    private const int SwShowMaximized = 3;

    public static bool ShouldHideWidget()
    {
        // 无歧义的系统状态：3=独占全屏(D3D) 4=演示模式
        int quns = QueryState();
        if (quns is 3 or 4) return true;

        return IsForegroundTrulyFullscreen();
    }

    /// <summary>描述当前前台窗口（类名/标题/位置/最大化状态），用于隐藏时写日志定位误报源。</summary>
    public static string DescribeForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "fg=null";
            var cls = new StringBuilder(64);
            GetClassNameW(hwnd, cls, cls.Capacity);
            var title = new StringBuilder(64);
            GetWindowTextW(hwnd, title, title.Capacity);
            GetWindowRect(hwnd, out var rect);
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hwnd, ref placement);
            return $"fg=[{cls}] \"{title}\" rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} showCmd={placement.showCmd}";
        }
        catch
        {
            return "fg=?";
        }
    }

    /// <summary>原始 QUNS 状态值（诊断用）；出错返回 -1。</summary>
    public static int QueryState()
    {
        try
        {
            return SHQueryUserNotificationState(out int state) == 0 ? state : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>前台窗口是否真正全屏（覆盖整块显示器、非最大化、非桌面/本程序）。</summary>
    public static bool IsForegroundTrulyFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return false;

            // 排除桌面/任务栏等 Shell 窗口
            var cls = new StringBuilder(64);
            GetClassNameW(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

            // 排除自身
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            // 最大化窗口不算（任务栏自动隐藏时最大化也会铺满，但用户仍在正常使用桌面）
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(hwnd, ref placement) && placement.showCmd == SwShowMaximized) return false;

            if (!GetWindowRect(hwnd, out var rect)) return false;

            // 显示器整屏矩形（用 MONITORINFO 的 rcMonitor，而非工作区）
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>(), szDevice = "" };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            const int tolerance = 2;
            return rect.Left <= info.rcMonitor.Left + tolerance
                && rect.Top <= info.rcMonitor.Top + tolerance
                && rect.Right >= info.rcMonitor.Right - tolerance
                && rect.Bottom >= info.rcMonitor.Bottom - tolerance;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);
}
