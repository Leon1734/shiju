using System.Diagnostics;
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

    /// <summary>启动 15 秒后后台检查更新（配置了地址才生效），有新版托盘气泡提示。</summary>
    private void ScheduleUpdateCheck()
    {
        if (string.IsNullOrWhiteSpace(AppServices.Settings.UpdateUrl)) return;
        var _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            var info = await UpdateChecker.CheckAsync(AppServices.Settings.UpdateUrl);
            if (info == null) return;
            Log.Info($"update: 发现新版本 {info.LatestVersion}");
            Dispatcher.Invoke(() =>
            {
                if (_tray == null) return;
                _tray.BalloonTipTitle = $"拾句有新版本 v{info.LatestVersion}";
                _tray.BalloonTipText = string.IsNullOrWhiteSpace(info.Notes) ? "点击查看下载地址" : info.Notes;
                _tray.BalloonTipClicked += OnUpdateBalloonClick;
                _tray.ShowBalloonTip(8000);
            });
        });
    }

    private void OnUpdateBalloonClick(object? sender, EventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AppServices.Settings.UpdateUrl))
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppServices.Settings.UpdateUrl,
                    UseShellExecute = true
                });
        }
        catch { }
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
            }
        };
    }

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
