using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using QuoteWidget.Models;
using QuoteWidget.Services;
using QuoteWidget.Views;
using WinForms = System.Windows.Forms;

namespace QuoteWidget;

public partial class App : Application
{
    private const string MutexName = @"Local\QuoteWidget_SingleInstance";
    private const string WakeName = @"Local\QuoteWidget_Wake";

    private Mutex? _mutex;
    private EventWaitHandle? _wake;
    private WinForms.NotifyIcon? _tray;
    private WidgetWindow? _widget;
    private SettingsWindow? _settingsWindow;
    private FavoritesWindow? _favoritesWindow;
    private TranslateWindow? _translateWindow;
    private HotkeyService? _hotkeys;

    /// <summary>新的 App 实例，用于在窗口/页面中直接访问应用级操作。</summary>
    public static new App Current => (App)System.Windows.Application.Current;

    public WidgetWindow? Widget => _widget;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;

        // 开机自启延迟：--autostart-delay=30（错开开机高峰；此期间已占单实例位）
        int delaySeconds = 0;
        foreach (var arg in e.Args)
        {
            var m = System.Text.RegularExpressions.Regex.Match(arg, @"^--autostart-delay=(\d+)$");
            if (m.Success) delaySeconds = int.Parse(m.Groups[1].Value);
        }

        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // 已有实例在运行：唤出它的挂件后自己退出
            try { EventWaitHandle.OpenExisting(WakeName).Set(); } catch { /* 首实例可能还没就绪 */ }
            Shutdown();
            return;
        }

        if (delaySeconds > 0)
        {
            Log.Info($"开机延迟启动：等待 {delaySeconds} 秒");
            Thread.Sleep(delaySeconds * 1000);
        }

        CleanUpdateCache();
        AppServices.Initialize();
        Log.Cleanup();
        Log.Info("拾句启动" + (delaySeconds > 0 ? $"（延迟 {delaySeconds}s）" : ""));
        if (AppServices.Settings.AutoStart)
            AutoStartService.Set(true, AppServices.Settings.AutostartDelaySeconds);

        WallpaperService.Initialize();

        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
        Task.Run(() =>
        {
            while (_wake.WaitOne())
            {
                try
                {
                    // 重复启动时唤出已有挂件，并顺手换一句新的
                    Dispatcher.Invoke(() =>
                    {
                        ShowWidget();
                        _widget?.NextQuote(forceNew: true);
                    });
                }
                catch { }
            }
        });

        HookSettingsSideEffects();

        _widget = new WidgetWindow();
        MainWindow = _widget;
        _widget.Show();

        InitTray();
        InitHotkeys();
        ScheduleUpdateCheck();

        // 文档/调试用：启动时直接打开指定窗口
        if (e.Args.Contains("--open-settings")) ShowSettings();
        if (e.Args.Contains("--open-translate")) ShowTranslate();
        if (e.Args.Contains("--open-favorites")) ShowFavorites();
    }

    /// <summary>清理上次升级留下的缓存文件（下载包/替换脚本）。</summary>
    private static void CleanUpdateCache()
    {
        try
        {
            if (!Directory.Exists(UpdateService.UpdateDir)) return;
            foreach (var file in Directory.GetFiles(UpdateService.UpdateDir))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    /// <summary>启动 15 秒后后台静默检查更新（配置了地址才生效）。</summary>
    private void ScheduleUpdateCheck()
    {
        if (string.IsNullOrWhiteSpace(AppServices.Settings.UpdateUrl)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            var info = await UpdateChecker.CheckAsync(AppServices.Settings.UpdateUrl);
            if (info == null) return;
            Log.Info($"update: 发现新版本 {info.LatestVersion}");
            Dispatcher.Invoke(() => ShowUpdateBalloon(info));
        });
    }

    private UpdateChecker.UpdateInfo? _pendingUpdate;

    /// <summary>托盘气泡提示有新版；点击后打开下载页（修复：以前误开清单地址）。</summary>
    private void ShowUpdateBalloon(UpdateChecker.UpdateInfo info)
    {
        if (_tray == null) return;
        _pendingUpdate = info;
        _tray.BalloonTipTitle = $"拾句有新版本 v{info.LatestVersion}（当前 v{UpdateChecker.CurrentVersion().ToString(3)}）";
        _tray.BalloonTipText = string.IsNullOrWhiteSpace(info.Notes) ? "点击打开下载页" : info.Notes;
        _tray.ShowBalloonTip(10000);
    }

    /// <summary>重启程序（导入配置后生效用）。</summary>
    public void RestartApp()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch { }
        Shutdown();
    }

    /// <summary>按需重建挂件窗口（亚克力/背景模式切换会影响窗口分层，无法热切换）。</summary>
    public void RecreateWidget()
    {
        try
        {
            var old = _widget;
            var wasVisible = old?.IsVisible ?? true;
            old?.SavePositionNow();
            var fresh = new WidgetWindow();
            _widget = fresh;
            MainWindow = fresh;
            old?.Close();
            if (wasVisible) fresh.Show();
            _hotkeys?.Attach(fresh);
            _hotkeys?.Apply(AppServices.Settings);
            Log.Info("widget: 已重建窗口（亚克力/背景模式切换）");
        }
        catch (Exception ex)
        {
            Log.Error("widget: 重建失败", ex);
        }
    }

    private void OnUpdateBalloonClick(object? sender, EventArgs e)
    {
        var info = _pendingUpdate;
        if (info == null) return;
        var target = !string.IsNullOrWhiteSpace(info.DownloadUrl)
            ? info.DownloadUrl
            : AppServices.Settings.UpdateUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>交互式检查更新（设置按钮 / 托盘菜单共用），返回结果文案。</summary>
    public async Task<string> CheckUpdatesInteractiveAsync()
    {
        if (string.IsNullOrWhiteSpace(AppServices.Settings.UpdateUrl))
            return "未配置更新地址。把更新清单 JSON 的 URL 填到「更新检查地址」即可启用。";

        var info = await UpdateChecker.CheckAsync(AppServices.Settings.UpdateUrl);
        if (info == null) return "已是最新版本，或更新地址暂不可访问。";
        Log.Info($"update: 发现新版本 {info.LatestVersion}");

        if (!string.IsNullOrWhiteSpace(info.FileUrl))
        {
            var go = MessageBox.Show(
                $"发现新版本 v{info.LatestVersion}（当前 v{UpdateChecker.CurrentVersion().ToString(3)}）\n\n{info.Notes}\n\n立即下载并升级？升级会自动重启程序，词库与设置不受影响。",
                "拾句 · 软件升级", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (go != MessageBoxResult.Yes) return "已取消升级。";
            var path = await UpdateService.DownloadAsync(info);
            UpdateService.ApplyAndRestart(path);
            return "正在升级并重启…";
        }

        ShowUpdateBalloon(info);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = info.DownloadUrl, UseShellExecute = true });
        }
        catch { }
        return $"发现新版本 v{info.LatestVersion}，已打开下载页。";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("拾句退出");
        try
        {
            _widget?.SavePositionNow();
            SettingsStore.Save(AppServices.Settings);
        }
        catch { }
        try { _hotkeys?.Dispose(); } catch { }
        try
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
        }
        catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>设置项变化需要的应用级副作用（挂件外观由 WidgetWindow 自行监听）。</summary>
    private void HookSettingsSideEffects()
    {
        AppServices.Settings.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(AppSettings.AutoStart):
                    AutoStartService.Set(AppServices.Settings.AutoStart, AppServices.Settings.AutostartDelaySeconds);
                    break;
                case nameof(AppSettings.AutostartDelaySeconds):
                    if (AppServices.Settings.AutoStart)
                        AutoStartService.Set(true, AppServices.Settings.AutostartDelaySeconds);
                    break;
                case nameof(AppSettings.ClickThrough):
                    _widget?.ApplyClickThrough();
                    break;
                case nameof(AppSettings.CatMovie):
                case nameof(AppSettings.CatGame):
                case nameof(AppSettings.CatNovel):
                case nameof(AppSettings.CatLyric):
                case nameof(AppSettings.RandomMode):
                    AppServices.Quotes.Reload(AppServices.Settings);
                    break;
                case nameof(AppSettings.AcrylicBackdrop):
                case nameof(AppSettings.BgMode):
                    // 亚克力与否决定窗口分层模式，无法热切换 → 重建挂件窗口
                    if (_widget != null && _widget.IsAcrylicMode != DesiredAcrylic(AppServices.Settings))
                        RecreateWidget();
                    break;
            }
        };
    }

    /// <summary>当前设置下是否应使用亚克力背景（需 Win11 22H2+，且非全透明模式）。</summary>
    public static bool DesiredAcrylic(AppSettings s) =>
        s.AcrylicBackdrop
        && s.BgMode != BackgroundMode.Transparent
        && Environment.OSVersion.Version.Build >= 22621;

    /// <summary>全局快捷键：挂在挂件窗口的消息钩子上。</summary>
    private void InitHotkeys()
    {
        _hotkeys = new HotkeyService();
        _hotkeys.Attach(_widget!);
        _hotkeys.Pressed += action =>
        {
            switch (action)
            {
                case HotkeyService.Action.Switch: _widget?.NextQuote(forceNew: true); break;
                case HotkeyService.Action.Favorite: _widget?.FavoriteCurrent(); break;
                case HotkeyService.Action.Toggle: ToggleWidget(); break;
            }
        };
        _hotkeys.Apply(AppServices.Settings);
        AppServices.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppSettings.HotkeySwitch)
                or nameof(AppSettings.HotkeyFavorite) or nameof(AppSettings.HotkeyToggle))
            {
                _hotkeys.Apply(AppServices.Settings);
            }
        };
    }

    private void InitTray()
    {
        var menu = new WinForms.ContextMenuStrip();

        menu.Items.Add("上一句", null, (_, _) => _widget?.PrevQuote());
        menu.Items.Add("下一句", null, (_, _) => _widget?.NextQuote(forceNew: false));
        menu.Items.Add("随机换句", null, (_, _) => _widget?.NextQuote(forceNew: true));
        menu.Items.Add("今日一句", null, (_, _) => _widget?.ShowDaily());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add("生成分享图", null, (_, _) => _widget?.ShareCurrent());
        menu.Items.Add("收藏当前句子", null, (_, _) => _widget?.FavoriteCurrent());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add("显示 / 隐藏挂件", null, (_, _) => ToggleWidget());
        menu.Items.Add("翻译小窗", null, (_, _) => ShowTranslate());
        menu.Items.Add("收藏夹", null, (_, _) => ShowFavorites());
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var clickThroughItem = new WinForms.ToolStripMenuItem("鼠标穿透")
        {
            CheckOnClick = true,
            Checked = AppServices.Settings.ClickThrough
        };
        clickThroughItem.Click += (_, _) => AppServices.Settings.ClickThrough = clickThroughItem.Checked;
        menu.Items.Add(clickThroughItem);

        var autoStartItem = new WinForms.ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = AppServices.Settings.AutoStart
        };
        autoStartItem.Click += (_, _) => AppServices.Settings.AutoStart = autoStartItem.Checked;
        menu.Items.Add(autoStartItem);

        var hitokotoItem = new WinForms.ToolStripMenuItem("一言在线补充")
        {
            CheckOnClick = true,
            Checked = AppServices.Settings.UseHitokoto
        };
        hitokotoItem.Click += (_, _) => AppServices.Settings.UseHitokoto = hitokotoItem.Checked;
        menu.Items.Add(hitokotoItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("检查更新", null, (_, _) => _ = CheckUpdatesInteractiveAsync());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        // 打开菜单前同步各勾选项的状态，避免设置窗口改动后托盘显示过期
        menu.Opening += (_, _) =>
        {
            clickThroughItem.Checked = AppServices.Settings.ClickThrough;
            autoStartItem.Checked = AppServices.Settings.AutoStart;
            hitokotoItem.Checked = AppServices.Settings.UseHitokoto;
        };

        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!.Stream;
        _tray = new WinForms.NotifyIcon
        {
            Text = "拾句 · 桌面名言",
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) ToggleWidget();
        };
        _tray.BalloonTipClicked += OnUpdateBalloonClick;
    }

    private void ToggleWidget()
    {
        if (_widget == null) return;
        if (_widget.Visibility == Visibility.Visible) _widget.Hide();
        else ShowWidget();
    }

    public void ShowWidget() => _widget?.Show();

    public void ShowQuote(Quote quote)
    {
        if (_widget == null) return;
        _widget.Show();
        _widget.DisplayQuote(quote);
    }

    public void ShowSettings(bool focusTranslation = false)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        if (focusTranslation) _settingsWindow.FocusTranslationSection();
        _settingsWindow.Activate();
    }

    public void ShowFavorites()
    {
        if (_favoritesWindow == null)
        {
            _favoritesWindow = new FavoritesWindow();
            _favoritesWindow.Closed += (_, _) => _favoritesWindow = null;
        }
        _favoritesWindow.Show();
        _favoritesWindow.Activate();
    }

    public void ShowTranslate(string? prefill = null)
    {
        if (_translateWindow == null)
        {
            _translateWindow = new TranslateWindow();
            _translateWindow.Closed += (_, _) => _translateWindow = null;
        }
        _translateWindow.Show();
        if (!string.IsNullOrWhiteSpace(prefill)) _translateWindow.Prefill(prefill);
        _translateWindow.Activate();
    }

    public void ExitApp()
    {
        _settingsWindow?.Close();
        _favoritesWindow?.Close();
        Shutdown();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI 线程异常", e.Exception);
        try
        {
            MessageBox.Show(
                "拾句遇到了一点小问题，但会继续运行。\n\n" + e.Exception.Message,
                "拾句", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
        e.Handled = true;
    }
}
