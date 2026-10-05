using System.Diagnostics;
using System.Windows.Threading;

namespace QuoteWidget.Services;

public enum ShutdownAction
{
    Shutdown,
    Restart,
    Sleep
}

/// <summary>
/// 定时关机：支持"N 分钟后"与"指定时刻（HH:mm）"，动作可选 关机/重启/睡眠。
/// 到点前 5 分钟与 60 秒各有一次可取消的提醒；关机/重启在最后 60 秒交给
/// Windows 自带的 shutdown 倒计时（系统通知 + shutdown /a 可取消），保证安全。
/// 计划会持久化到设置里，程序重启后继续生效。
/// </summary>
public static class ShutdownService
{
    private static DispatcherTimer? _timer;
    private static bool _systemCountdownStarted;
    private static bool _warnedFiveMinutes;

    /// <summary>状态变化（设置/取消/进入系统倒计时），用于刷新托盘菜单与设置页文案。</summary>
    public static event Action? StateChanged;

    /// <summary>需要提醒用户（如 5 分钟后关机）；cancellable 表示点击可取消。</summary>
    public static event Action<string, bool>? Warning;

    public static DateTime? ScheduledAt => AppServices.Settings.ShutdownAt;

    public static ShutdownAction Action =>
        Enum.TryParse<ShutdownAction>(AppServices.Settings.ShutdownAction, out var a) ? a : ShutdownAction.Shutdown;

    public static bool IsScheduled => ScheduledAt != null;

    public static string ActionName(ShutdownAction action) => action switch
    {
        ShutdownAction.Restart => "重启",
        ShutdownAction.Sleep => "睡眠",
        _ => "关机"
    };

    public static string RemainingText =>
        ScheduledAt is { } at ? FormatRemaining(at - DateTime.Now) : "";

    /// <summary>应用启动时恢复未完成的计划（过期超过 2 分钟的计划作废）。</summary>
    public static void Initialize()
    {
        var s = AppServices.Settings;
        if (s.ShutdownAt is { } at)
        {
            if (at > DateTime.Now.AddMinutes(-2))
            {
                Log.Info($"shutdown: 恢复定时计划 {at:yyyy-MM-dd HH:mm:ss} {ActionName(Action)}");
                StartWatcher();
            }
            else
            {
                s.ShutdownAt = null;
                SettingsStore.Save(s);
            }
        }
    }

    /// <summary>设定定时计划。</summary>
    public static void Schedule(DateTime at, ShutdownAction action)
    {
        var s = AppServices.Settings;
        s.ShutdownAt = at;
        s.ShutdownAction = action.ToString();
        SettingsStore.Save(s);
        _systemCountdownStarted = false;
        _warnedFiveMinutes = false;
        Log.Info($"shutdown: 已设置 {at:yyyy-MM-dd HH:mm:ss} {ActionName(action)}");
        StartWatcher();
        StateChanged?.Invoke();
        Warning?.Invoke($"已设置 {FormatRemaining(at - DateTime.Now)}后{ActionName(action)}（可随时取消）", true);
    }

    /// <summary>取消计划；若已进入系统倒计时则执行 shutdown /a。</summary>
    public static void Cancel()
    {
        bool wasSystem = _systemCountdownStarted;
        _timer?.Stop();
        _systemCountdownStarted = false;
        _warnedFiveMinutes = false;

        var s = AppServices.Settings;
        if (s.ShutdownAt != null)
        {
            s.ShutdownAt = null;
            SettingsStore.Save(s);
        }
        if (wasSystem)
        {
            try { Process.Start(new ProcessStartInfo("shutdown.exe", "/a") { CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        }
        Log.Info("shutdown: 计划已取消");
        StateChanged?.Invoke();
    }

    private static void StartWatcher()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick -= Tick;   // 防重复订阅
        _timer.Tick += Tick;
        _timer.Start();
    }

    private static void Tick(object? sender, EventArgs e)
    {
        if (ScheduledAt is not { } at)
        {
            _timer?.Stop();
            return;
        }

        var remaining = at - DateTime.Now;

        // 已过期较久（已触发过系统倒计时、或程序恢复时已过点）：清理计划并停表，避免空转
        if (remaining <= TimeSpan.FromMinutes(-2))
        {
            _timer?.Stop();
            ClearPlan();
            return;
        }

        // 5 分钟提醒（可取消）
        if (!_warnedFiveMinutes && remaining <= TimeSpan.FromMinutes(5) && remaining > TimeSpan.FromSeconds(70))
        {
            _warnedFiveMinutes = true;
            Warning?.Invoke($"{FormatRemaining(remaining)}后将{ActionName(Action)}，点击此提示可取消", true);
        }

        // 最后 60 秒：关机/重启交给系统倒计时（系统会弹通知，可用 shutdown /a 取消）
        if (!_systemCountdownStarted && remaining <= TimeSpan.FromSeconds(60))
        {
            _systemCountdownStarted = true;
            if (Action == ShutdownAction.Sleep)
            {
                Warning?.Invoke("60 秒后进入睡眠，点击此提示可取消", true);
            }
            else
            {
                var flag = Action == ShutdownAction.Restart ? "/r" : "/s";
                RunShutdown($"{flag} /t 60 /c \"拾句定时{ActionName(Action)}\"");
                Warning?.Invoke($"60 秒后{ActionName(Action)}（系统倒计时已开始，点击此提示可取消）", true);
            }
            StateChanged?.Invoke();
        }

        // 睡眠到点直接执行（睡眠无法由系统延迟）
        if (Action == ShutdownAction.Sleep && remaining <= TimeSpan.Zero)
        {
            _timer?.Stop();
            ClearPlan();
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Log.Error("shutdown: 睡眠执行失败", ex);
            }
        }
    }

    private static void ClearPlan()
    {
        var s = AppServices.Settings;
        s.ShutdownAt = null;
        SettingsStore.Save(s);
        StateChanged?.Invoke();
    }

    private static void RunShutdown(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            Log.Info("shutdown: 已执行 shutdown.exe " + args);
        }
        catch (Exception ex)
        {
            Log.Error("shutdown: 执行失败", ex);
        }
    }

    /// <summary>把剩余时间格式化为友好文本。</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "即将";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分钟";
        if (remaining.TotalMinutes >= 1)
            return $"{remaining.Minutes} 分钟";
        return $"{remaining.Seconds} 秒";
    }

    /// <summary>把 "HH:mm" 解析为下一个该时刻（已过则顺延到明天）。</summary>
    public static bool TryParseClock(string text, out DateTime at)
    {
        at = default;
        var parts = text.Trim().Split(':', '：');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute)) return false;
        if (hour is < 0 or > 23 || minute is < 0 or > 59) return false;

        var now = DateTime.Now;
        at = now.Date.AddHours(hour).AddMinutes(minute);
        if (at <= now) at = at.AddDays(1);
        return true;
    }
}
