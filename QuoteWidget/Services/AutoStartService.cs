using Microsoft.Win32;

namespace QuoteWidget.Services;

/// <summary>开机自启：写当前用户的 Run 注册表项，不需要管理员权限。</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuoteWidget";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enable, int delaySeconds = 0)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    // 延迟启动：由程序自身在启动参数里等待，错开开机高峰
                    var value = delaySeconds > 0
                        ? $"\"{exe}\" --autostart-delay={delaySeconds}"
                        : $"\"{exe}\"";
                    key.SetValue(ValueName, value);
                }
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }
}
