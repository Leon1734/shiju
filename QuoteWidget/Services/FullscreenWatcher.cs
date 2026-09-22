using System.Runtime.InteropServices;

namespace QuoteWidget.Services;

/// <summary>
/// 全屏/演示模式检测：用 shell32 的 SHQueryUserNotificationState 判断
/// 当前是否处于"全屏应用 / D3D 独占全屏 / 演示模式 / 锁屏"状态，用于自动隐藏挂件。
/// </summary>
public static class FullscreenWatcher
{
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    // 状态值：1=锁屏/屏保 2=全屏应用 3=D3D 独占全屏 4=演示模式 5=正常 6=专注 7=应用(App)
    // 只在 2/3/4（用户在场但被全屏遮挡）时隐藏；锁屏(1)保持可见，避免"消失了不回来"的风险。
    public static bool ShouldHideWidget()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) != 0) return false;
            return state is 2 or 3 or 4;
        }
        catch
        {
            return false;
        }
    }
}
