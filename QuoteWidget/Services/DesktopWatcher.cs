using System.Runtime.InteropServices;
using System.Text;

namespace QuoteWidget.Services;

/// <summary>
/// 桌面状态检测：判断当前"前台窗口"是否就是桌面本身
/// （桌面可见 = 用户没有在使用任何应用窗口），用于"只在桌面显示"模式。
/// </summary>
public static class DesktopWatcher
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int max);

    /// <summary>前台是否为桌面/任务栏/本程序（含：无前台窗口、桌面 Progman、WorkerW、开始菜单任务栏、本进程窗口）。</summary>
    public static bool IsDesktopForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return true; // 挂件/设置窗口在前面时也算"自己的地盘"

            var cls = new StringBuilder(64);
            GetClassNameW(hwnd, cls, cls.Capacity);
            return cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }
        catch
        {
            return true; // 出错时按桌面处理，宁可显示不要误藏
        }
    }

    /// <summary>前台窗口描述（日志诊断用）。</summary>
    public static string DescribeForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "无前台窗口(桌面)";
            var cls = new StringBuilder(64);
            GetClassNameW(hwnd, cls, cls.Capacity);
            GetWindowThreadProcessId(hwnd, out uint pid);
            return $"[{cls}] pid={pid}";
        }
        catch
        {
            return "?";
        }
    }
}
