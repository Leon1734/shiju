using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using QuoteWidget.Models;
using QuoteWidget.Services;
using QuoteWidget.ViewModels;

namespace QuoteWidget.Views;

public partial class WidgetWindow : Window
{
    /// <summary>出处行不透明度：彩虹模式下全不透明，颜色更饱满。</summary>
    private double SourceOpacity => AppServices.Settings.RainbowText ? 1.0 : 0.85;

    private readonly WidgetViewModel _vm = new();
    private readonly DispatcherTimer _autoTimer = new();
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _typeTimer = new();
    private readonly DispatcherTimer _minuteTimer = new();
    private readonly DispatcherTimer _fullscreenTimer = new();
    private DispatcherTimer? _onboardingTimer;
    private bool _hiddenByFullscreen;
    private DateTime _dailyDate = DateTime.Now.Date;
    private int _lastReloadHour = -1;
    private string _typeTarget = "";
    private int _typePos;

    private bool _switching;
    private bool _started;
    private bool _hovering;
    private bool _dragging;
    private Point _mouseDownPoint;

    /// <summary>当前窗口是否运行在亚克力背景模式（由 App 在重建时决定）。</summary>
    public bool IsAcrylicMode { get; }

    private System.ComponentModel.PropertyChangedEventHandler? _settingsHandler;
    private Action? _favoritesHandler;
    private Action? _wallpaperHandler;
    private Microsoft.Win32.PowerModeChangedEventHandler? _powerHandler;
    private Microsoft.Win32.SessionSwitchEventHandler? _sessionHandler;

    public WidgetWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // 亚克力背景需要非分层窗口（Windows 11 22621+），与全透明模式的 AllowsTransparency 互斥，
        // 因此本模式在窗口创建时决定；切换该设置由 App 负责重建窗口。
        IsAcrylicMode = AppServices.Settings.AcrylicBackdrop
                        && AppServices.Settings.BgMode != BackgroundMode.Transparent
                        && Environment.OSVersion.Version.Build >= 22621;
        if (IsAcrylicMode)
        {
            AllowsTransparency = false;
            Background = System.Windows.Media.Brushes.Transparent;
        }

        _autoTimer.Tick += async (_, _) => await SwitchQuoteAsync(forceNew: false);

        _toastTimer.Interval = TimeSpan.FromSeconds(1.3);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };

        _typeTimer.Tick += TypeTick;

        // 每 4 秒检测全屏应用/演示模式：是则自动隐藏，退出后自动恢复
        _fullscreenTimer.Interval = TimeSpan.FromSeconds(4);
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();

        // 每分钟心跳：处理勿扰时段边界、每日一句跨零点、定时词库跨时段换池
        _minuteTimer.Interval = TimeSpan.FromMinutes(1);
        _minuteTimer.Tick += (_, _) =>
        {
            var s = AppServices.Settings;
            if (DateTime.Now.Hour != _lastReloadHour)
            {
                _lastReloadHour = DateTime.Now.Hour;
                AppServices.Quotes.Reload(s); // 定时词库：跨时段自动换池
            }
            UpdateAutoTimer();
            _vm.RefreshHolidayBadge();
            if (s.DailyMode && DateTime.Now.Date != _dailyDate)
            {
                _dailyDate = DateTime.Now.Date;
                ShowDaily();
            }
        };

        _settingsHandler = (_, _) => ApplySettings();
        AppServices.Settings.PropertyChanged += _settingsHandler;
        _favoritesHandler = () => Dispatcher.Invoke(_vm.RefreshFavorite);
        AppServices.Favorites.Changed += _favoritesHandler;
        _wallpaperHandler = () => Dispatcher.Invoke(ApplySettings);
        WallpaperService.Changed += _wallpaperHandler;

        // 隐藏时暂停一切定时与动画（省电）；唤醒/解锁后自愈恢复
        IsVisibleChanged += (_, _) => SetActive(IsVisible);
        try
        {
            _powerHandler = (_, args) =>
            {
                if (args.Mode == Microsoft.Win32.PowerModes.Resume) OnWakeRecover();
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += _powerHandler;
            _sessionHandler = (_, args) =>
            {
                if (args.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) OnWakeRecover();
            };
            Microsoft.Win32.SystemEvents.SessionSwitch += _sessionHandler;
        }
        catch { }

        ApplySettings();
        ApplyStoredPosition();

        Loaded += (_, _) =>
        {
            _lastReloadHour = DateTime.Now.Hour;
            UpdateAutoTimer();
            _minuteTimer.Start();
            _fullscreenTimer.Start();
            _vm.RefreshHolidayBadge();
            if (AppServices.Settings.DailyMode)
                ShowDaily();
            else
                _ = SwitchQuoteAsync(forceNew: true);
            _ = MaybeShowOnboardingAsync();
        };
        ContentRendered += (_, _) => ClampIntoScreen();
        SourceInitialized += (_, _) =>
        {
            ApplyClickThrough();
            ApplyAcrylicBackdrop();
        };
    }

    /// <summary>窗口关闭（仅窗口重建/退出时）解除所有长时间订阅，避免悬挂回调。</summary>
    protected override void OnClosed(EventArgs e)
    {
        _autoTimer.Stop();
        _minuteTimer.Stop();
        _fullscreenTimer.Stop();
        _toastTimer.Stop();
        _typeTimer.Stop();
        _onboardingTimer?.Stop();
        if (_settingsHandler != null) AppServices.Settings.PropertyChanged -= _settingsHandler;
        if (_favoritesHandler != null) AppServices.Favorites.Changed -= _favoritesHandler;
        if (_wallpaperHandler != null) WallpaperService.Changed -= _wallpaperHandler;
        try
        {
            if (_powerHandler != null) Microsoft.Win32.SystemEvents.PowerModeChanged -= _powerHandler;
            if (_sessionHandler != null) Microsoft.Win32.SystemEvents.SessionSwitch -= _sessionHandler;
        }
        catch { }
        base.OnClosed(e);
    }

    // ———————— 亚克力背景（Windows 11） ————————

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>启用系统亚克力背景 + 系统圆角（仅 Windows 11 22H2+）。</summary>
    private void ApplyAcrylicBackdrop()
    {
        if (!IsAcrylicMode) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            const int DwmwaSystemBackdropType = 38;
            const int DwmwaWindowCornerPreference = 33;
            int acrylic = 3; // DWMSBT_TRANSIENTWINDOW
            int round = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref acrylic, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
            Log.Info("acrylic: 已启用亚克力背景");
        }
        catch (Exception ex)
        {
            Log.Warn("acrylic: 启用失败，回退普通背景 " + ex.Message);
        }
    }

    // ———————— 全屏自动隐藏 / 唤醒恢复 / 可见性 ————————

    /// <summary>全屏应用/演示模式时自动隐藏，退出全屏后恢复（设置可关）。</summary>
    private void CheckFullscreen()
    {
        var s = AppServices.Settings;
        if (!s.AutoHideFullscreen)
        {
            if (_hiddenByFullscreen)
            {
                _hiddenByFullscreen = false;
                Show();
            }
            return;
        }

        bool busy = FullscreenWatcher.ShouldHideWidget();
        if (busy && IsVisible)
        {
            _hiddenByFullscreen = true;
            Log.Info("fullscreen: 检测到全屏应用，挂件自动隐藏");
            Hide();
        }
        else if (!busy && _hiddenByFullscreen)
        {
            _hiddenByFullscreen = false;
            Log.Info("fullscreen: 退出全屏，挂件恢复显示");
            Show();
        }
    }

    /// <summary>睡眠唤醒 / 解锁后自愈：重采样壁纸、复位动画、恢复定时。</summary>
    private void OnWakeRecover()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnWakeRecover);
            return;
        }
        try
        {
            Log.Info("power: 唤醒/解锁，恢复运行状态");
            WallpaperService.Resample();
            StopTypewriter();
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = 1;
            _vm.SetPreviewText(_vm.CurrentQuote?.Text ?? _vm.Text);
            ApplySettings();
            UpdateAutoTimer();
            CheckFullscreen();
        }
        catch { }
    }

    /// <summary>可见性联动：隐藏时停掉定时器/动画，显示时恢复。</summary>
    private void SetActive(bool active)
    {
        if (active)
        {
            if (!_minuteTimer.IsEnabled) _minuteTimer.Start();
            if (!_fullscreenTimer.IsEnabled) _fullscreenTimer.Start();
            UpdateAutoTimer();
        }
        else
        {
            _autoTimer.Stop();
            _minuteTimer.Stop();
            _fullscreenTimer.Stop();
            _toastTimer.Stop();
            StopTypewriter();
        }
    }

    // ———————— 换句与动画 ————————

    /// <summary>切到下一句；forceNew=false 时若历史中还有"下一步"则先走历史。</summary>
    public async Task SwitchQuoteAsync(bool forceNew)
    {
        if (_switching) return;
        _switching = true;
        try
        {
            bool forward = !forceNew && _vm.CanForward;
            Task<Quote?>? fetch = forward ? null : _vm.FetchNewAsync();

            if (_started)
                await FadeOutAsync();
            _started = true;

            if (forward)
            {
                var moved = _vm.MoveForward();
                if (moved != null) PresentQuote(moved);
            }
            else
            {
                var quote = await fetch!;
                if (quote == null)
                {
                    FadeIn();
                    return;
                }
                PresentQuote(quote);
            }
            ClampIntoScreen();
            FadeIn();
        }
        finally
        {
            _switching = false;
        }
    }

    public void NextQuote(bool forceNew) => _ = SwitchQuoteAsync(forceNew);

    public async void PrevQuote()
    {
        if (_switching || !_vm.CanBack) return;
        _switching = true;
        try
        {
            await FadeOutAsync();
            var moved = _vm.MoveBack();
            if (moved != null) PresentQuote(moved);
            ClampIntoScreen();
            FadeIn();
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>外部定位展示（收藏夹双击跳转），不写入浏览历史。</summary>
    public async void DisplayQuote(Quote quote)
    {
        if (_switching) return;
        _switching = true;
        try
        {
            await FadeOutAsync();
            PresentQuote(quote);
            ClampIntoScreen();
            FadeIn();
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>今日一句（每日一句模式下启动与跨零点调用）。</summary>
    public async void ShowDaily()
    {
        if (_switching) return;
        _switching = true;
        try
        {
            await FadeOutAsync();
            var quote = _vm.FetchDaily();
            if (quote != null)
            {
                PresentQuote(quote);
                ClampIntoScreen();
            }
            FadeIn();
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>设置当前句子内容并按设置决定是否打字机逐字显示。</summary>
    private void PresentQuote(Quote quote)
    {
        _vm.SetCurrent(quote);
        _vm.SetTranslation(null); // 换句清空上一句的译文
        StopTypewriter();
        _foldedFirstLine = null;

        if (AppServices.Settings.FoldMultiLine && quote.Text.Contains('\n'))
        {
            // 折叠模式：只显示第一行
            _foldedFirstLine = quote.Text.Split('\n')[0].Trim();
            _vm.SetPreviewText(_foldedFirstLine);
            ClampIntoScreen();
            return;
        }

        if (AppServices.Settings.Typewriter && quote.Text.Length > 0)
            StartTypewriter(quote.Text);
    }

    /// <summary>翻译当前句子并把译文显示在挂件上。</summary>
    public async void TranslateCurrent()
    {
        var quote = _vm.CurrentQuote;
        if (quote == null || _translating) return;
        _translating = true;
        try
        {
            ShowToast("翻译中…");
            var result = await TranslationService.TranslateAsync(quote.Text);
            if (result.Success)
            {
                _vm.SetTranslation(result.Output);
                ClampIntoScreen();
                ShowToast("译文已显示");
            }
            else
            {
                ShowToast(result.Error ?? "翻译失败");
            }
        }
        finally
        {
            _translating = false;
        }
    }

    private bool _translating;

    private void OnTranslateClick(object sender, RoutedEventArgs e) => TranslateCurrent();

    /// <summary>打开翻译小窗并带入当前句子（小窗内可用全部大模型引擎）。</summary>
    private void OnOpenTranslateClick(object sender, RoutedEventArgs e)
        => App.Current.ShowTranslate(_vm.CurrentQuote?.Text);

    private void OnMenuTranslateWindowClick(object sender, RoutedEventArgs e)
        => App.Current.ShowTranslate(_vm.CurrentQuote?.Text);

    private Task FadeOutAsync()
    {
        var tcs = new TaskCompletionSource();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
        anim.Completed += (_, _) => tcs.TrySetResult();
        Root.BeginAnimation(OpacityProperty, anim);
        return tcs.Task;
    }

    private void FadeIn()
    {
        var duration = TimeSpan.FromMilliseconds(480);
        var ease = new CubicEase();
        var opacity = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var slide = new DoubleAnimation(14, 0, duration) { EasingFunction = ease };
        var scale = new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease };
        RootTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        Root.BeginAnimation(OpacityProperty, opacity);
    }

    // ———————— 打字机逐字显示 ————————

    private void StartTypewriter(string text)
    {
        _typeTarget = text;
        _typePos = 0;
        _vm.SetPreviewText("");
        SourceText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(1)));
        // 全文约 0.9 秒打完，单字间隔限制在 14~50ms
        _typeTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(900.0 / _typeTarget.Length, 14, 50));
        _typeTimer.Start();
    }

    private void TypeTick(object? sender, EventArgs e)
    {
        _typePos++;
        if (_typePos >= _typeTarget.Length)
        {
            _typeTimer.Stop();
            _vm.SetPreviewText(_typeTarget);
            SourceText.BeginAnimation(OpacityProperty, new DoubleAnimation(SourceOpacity, TimeSpan.FromMilliseconds(300)));
            ClampIntoScreen();
            return;
        }
        _vm.SetPreviewText(_typeTarget[.._typePos]);
    }

    private void StopTypewriter()
    {
        if (!_typeTimer.IsEnabled) return;
        _typeTimer.Stop();
        _vm.SetPreviewText(_typeTarget);
        SourceText.BeginAnimation(OpacityProperty, null);
        SourceText.Opacity = SourceOpacity;
    }

    // ———————— 自动换句定时器 ————————

    public void UpdateAutoTimer()
    {
        _autoTimer.Stop();
        var s = AppServices.Settings;
        if (s.DailyMode) return; // 每日一句由分钟心跳负责跨零点
        if (!s.AutoSwitch || s.AutoSwitchSeconds <= 0) return;
        if (InDoNotDisturb()) return;
        _autoTimer.Interval = TimeSpan.FromSeconds(s.AutoSwitchSeconds);
        if (!_hovering || !s.PauseOnHover) _autoTimer.Start();
    }

    /// <summary>当前是否处于勿扰时段（支持跨零点，如 23 点到 7 点）。</summary>
    private static bool InDoNotDisturb()
    {
        var s = AppServices.Settings;
        if (!s.DndEnabled || s.DndStartHour == s.DndEndHour) return false;
        int hour = DateTime.Now.Hour;
        return s.DndStartHour < s.DndEndHour
            ? hour >= s.DndStartHour && hour < s.DndEndHour
            : hour >= s.DndStartHour || hour < s.DndEndHour;
    }

    // ———————— 外观应用 ————————

    public void ApplySettings()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ApplySettings);
            return;
        }

        var s = AppServices.Settings;

        Width = Math.Max(280, s.WidgetWidth);

        bool transparent = s.BgMode == BackgroundMode.Transparent;
        bool adaptive = transparent && s.AdaptiveTextColor;
        bool darkWallpaper = WallpaperService.Luminance < 0.5;
        var textColor = adaptive
            ? (darkWallpaper ? Colors.White : Color.FromRgb(0x2D, 0x34, 0x36))
            : ParseColor(s.TextColor, "#FFFFFF");
        Root.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(textColor));
        QuoteText.FontFamily = new FontFamily(s.FontFamily);
        QuoteText.FontSize = s.FontSize;
        QuoteText.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        SourceText.FontFamily = QuoteText.FontFamily;
        SourceText.FontSize = Math.Max(11, s.FontSize - 3);
        TranslationText.FontFamily = QuoteText.FontFamily;
        TranslationText.Effect = QuoteText.Effect;
        QuoteText.LineHeight = s.FontSize * s.LineSpacing;

        // 文字颜色：七彩彩虹（正文+出处+译文全部）> 单色渐变 > 纯色
        Root.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(textColor));
        if (s.RainbowText)
        {
            QuoteText.Foreground = MakeRainbowBrush();
            SourceText.Foreground = MakeRainbowBrush();
            TranslationText.Foreground = MakeRainbowBrush();
        }
        else
        {
            QuoteText.Foreground = s.GradientText ? MakeTextGradient(textColor) : null;
            SourceText.Foreground = null;
            TranslationText.Foreground = null;
        }
        if (!_typeTimer.IsEnabled)
        {
            SourceText.Opacity = SourceOpacity;
            SourceText.BeginAnimation(OpacityProperty, null);
        }

        Card.Visibility = transparent ? Visibility.Collapsed : Visibility.Visible;
        QuoteText.Effect = transparent ? MakeTextShadow(darkWallpaper) : null;
        SourceText.Effect = QuoteText.Effect;

        if (!transparent)
        {
            var baseColor = ParseColor(s.BgColor, "#2D2D3A");
            var alpha = (byte)Math.Clamp(
                s.BgMode == BackgroundMode.Translucent ? s.BgOpacity * 255 * 0.45 : s.BgOpacity * 255,
                8, 255);
            if (IsAcrylicMode)
            {
                // 亚克力模式：系统圆角 + 更透的色调，让模糊背景透出来
                Card.CornerRadius = new CornerRadius(0);
                var soft = (byte)Math.Clamp(s.BgOpacity * 255 * 0.7, 24, 190);
                Card.Background = new SolidColorBrush(Color.FromArgb(soft, baseColor.R, baseColor.G, baseColor.B));
                Card.Effect = null;
            }
            else
            {
                Card.Background = MakeFlowGradient(baseColor, alpha);
                Card.CornerRadius = new CornerRadius(s.CornerRadius);
                Card.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.35 };
            }
        }
        else
        {
            Card.Effect = null;
        }

        Topmost = s.Topmost;
        UpdateAutoTimer();
        ApplyFoldState();
    }

    /// <summary>多行句折叠：只显示第一行，悬停展开全文（打字机在折叠时停用）。</summary>
    private void ApplyFoldState()
    {
        var quote = _vm.CurrentQuote;
        if (quote == null || _typeTimer.IsEnabled) return;
        if (AppServices.Settings.FoldMultiLine && quote.Text.Contains('\n'))
        {
            _foldedFirstLine = quote.Text.Split('\n')[0].Trim();
            _vm.SetPreviewText(_foldedFirstLine);
        }
        else
        {
            _foldedFirstLine = null;
            _vm.SetPreviewText(quote.Text);
        }
    }

    private string? _foldedFirstLine;

    private static Brush MakeTextGradient(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(ShiftColor(color, 0.45), 0));
        brush.GradientStops.Add(new GradientStop(color, 0.55));
        brush.GradientStops.Add(new GradientStop(ShiftColor(color, -0.12), 1));
        return brush;
    }

    /// <summary>彩虹渐变笔刷：七色横向铺满正文区域。</summary>
    private static Brush MakeRainbowBrush()
    {
        Color[] colors =
        {
            Color.FromRgb(0xFF, 0x5A, 0x5A), // 红
            Color.FromRgb(0xFF, 0x9F, 0x43), // 橙
            Color.FromRgb(0xF3, 0xD2, 0x4E), // 黄
            Color.FromRgb(0x2E, 0xCC, 0x71), // 绿
            Color.FromRgb(0x45, 0xAA, 0xF2), // 蓝
            Color.FromRgb(0x6C, 0x5C, 0xE7), // 靛
            Color.FromRgb(0xE8, 0x43, 0x93), // 紫
        };
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        for (int i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], (double)i / (colors.Length - 1)));
        return brush;
    }

    internal static Color ShiftColor(Color c, double amount)
    {
        byte F(byte v) => (byte)Math.Clamp(amount > 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    /// <summary>流光渐变卡片：主色向亮/暗各延伸一档，渐变方向缓慢旋转。</summary>
    private static Brush MakeFlowGradient(Color baseColor, byte alpha)
    {
        Color Shift(Color c, double amount)
        {
            byte F(byte v) => (byte)Math.Clamp(amount > 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
            return Color.FromArgb(alpha, F(c.R), F(c.G), F(c.B));
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            SpreadMethod = GradientSpreadMethod.Pad
        };
        brush.GradientStops.Add(new GradientStop(Shift(baseColor, 0.14), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B), 0.5));
        brush.GradientStops.Add(new GradientStop(Shift(baseColor, -0.14), 1));
        brush.RelativeTransform = new RotateTransform(0, 0.5, 0.5);
        brush.RelativeTransform.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(36)) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    /// <summary>字幕式光环阴影：ShadowDepth=0 的柔光描边。深色壁纸白字配黑光环，浅色壁纸墨字配白光环。</summary>
    private static Effect MakeTextShadow(bool darkWallpaper) => new DropShadowEffect
    {
        BlurRadius = 8,
        ShadowDepth = 0,
        Opacity = 1.0,
        Color = darkWallpaper ? Colors.Black : Colors.White
    };

    private static Color ParseColor(string hex, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    // ———————— 位置与穿透 ————————

    private void ApplyStoredPosition()
    {
        var s = AppServices.Settings;

        // 优先：按当前显示器记住的位置（多屏场景各自记忆）
        try
        {
            foreach (var device in MonitorHelper.AllDeviceNames())
            {
                if (s.WindowPositions.TryGetValue(device, out var pos) && pos is { Length: 2 })
                {
                    Left = pos[0];
                    Top = pos[1];
                    ClampIntoScreen();
                    return;
                }
            }
        }
        catch { }

        if (s.WindowLeft is { } left && s.WindowTop is { } top)
        {
            Left = left;
            Top = top;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 48;
            Top = workArea.Bottom - 260;
        }
        ClampIntoScreen();
    }

    private void ClampIntoScreen()
    {
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsWidth = SystemParameters.VirtualScreenWidth;
        double vsHeight = SystemParameters.VirtualScreenHeight;

        Left = Math.Clamp(Left, vsLeft + 4, Math.Max(vsLeft + 4, vsLeft + vsWidth - ActualWidth - 4));
        Top = Math.Clamp(Top, vsTop + 4, Math.Max(vsTop + 4, vsTop + vsHeight - EstimatedHeight() - 4));
    }

    private double EstimatedHeight() => Math.Max(ActualHeight, 140);

    public void SavePositionNow()
    {
        var s = AppServices.Settings;
        s.WindowLeft = Left;
        s.WindowTop = Top;
        try
        {
            // 记录到当前显示器名下（DIP -> 物理像素定位显示器）
            var dpi = VisualTreeHelper.GetDpi(this);
            var device = MonitorHelper.DeviceNameAt(
                (int)((Left + Width / 2) * dpi.DpiScaleX),
                (int)((Top + 10) * dpi.DpiScaleY));
            if (!string.IsNullOrEmpty(device))
                s.WindowPositions[device] = new[] { Left, Top };
        }
        catch { }
        SettingsStore.Save(s);
    }

    public void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (IsAcrylicMode)
        {
            // 亚克力依赖非分层窗口，穿透所需的 LAYERED 会破坏模糊效果，此处仅保留 Alt+Tab 隐藏
            int acrylicStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, acrylicStyle | WS_EX_TOOLWINDOW);
            return;
        }
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW; // 不出现在 Alt+Tab
        style = AppServices.Settings.ClickThrough
            ? style | WS_EX_TRANSPARENT
            : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
        if (AppServices.Settings.ClickThrough) ShowControls(false);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    // ———————— 鼠标交互 ————————

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        ShowControls(true);
        if (_foldedFirstLine != null)
        {
            _vm.SetPreviewText(_vm.CurrentQuote?.Text ?? _foldedFirstLine); // 展开全文
            ClampIntoScreen();
        }
        if (AppServices.Settings.PauseOnHover) _autoTimer.Stop();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        ShowControls(false);
        if (_foldedFirstLine != null)
        {
            _vm.SetPreviewText(_foldedFirstLine); // 恢复折叠
            ClampIntoScreen();
        }
        UpdateAutoTimer();
    }

    private void ShowControls(bool show)
    {
        ControlBar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(180)));
        ControlBar.IsHitTestVisible = show;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (OnboardingTip.Visibility == Visibility.Visible)
        {
            DismissOnboarding(); // 点任意处关闭引导
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Middle)
        {
            NextQuote(forceNew: true); // 中键随机换句
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2)
        {
            CopyCurrent();
            e.Handled = true;
            return;
        }
        _mouseDownPoint = e.GetPosition(this);
        _dragging = false;
    }

    // ———————— 首启引导卡片 ————————

    private async Task MaybeShowOnboardingAsync()
    {
        if (AppServices.Settings.OnboardingShown) return;
        await Task.Delay(1400); // 等首句动画结束再弹
        if (!IsVisible) return;
        OnboardingTip.Visibility = Visibility.Visible;
        OnboardingTip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)));
        _onboardingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _onboardingTimer.Tick += (_, _) => DismissOnboarding();
        _onboardingTimer.Start();
    }

    private void DismissOnboarding()
    {
        if (OnboardingTip.Visibility != Visibility.Visible) return;
        _onboardingTimer?.Stop();
        _onboardingTimer = null;
        OnboardingTip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        OnboardingTip.IsHitTestVisible = false;
        var collapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(460) };
        collapse.Tick += (_, _) =>
        {
            collapse.Stop();
            OnboardingTip.Visibility = Visibility.Collapsed;
        };
        collapse.Start();

        AppServices.Settings.OnboardingShown = true;
        SettingsStore.Save(AppServices.Settings);
    }

    /// <summary>悬停时滚轮换句：向上滚 = 上一句，向下滚 = 下一句。</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (e.Delta > 0) PrevQuote();
        else NextQuote(forceNew: false);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _dragging = true;
        try { DragMove(); } catch { /* 窗口失活等场景下 DragMove 会抛异常，忽略 */ }
        _dragging = false;
        SavePositionNow();
        ClampIntoScreen();
    }

    // ———————— 按钮命令 ————————

    private void OnPrevClick(object sender, RoutedEventArgs e) => PrevQuote();

    private void OnNextClick(object sender, RoutedEventArgs e) => NextQuote(forceNew: false);

    private void OnRandomClick(object sender, RoutedEventArgs e) => NextQuote(forceNew: true);

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyCurrent();

    /// <summary>收藏/取消收藏当前句子（悬停按钮、右键菜单、全局快捷键共用）。</summary>
    public void FavoriteCurrent()
    {
        var quote = _vm.CurrentQuote;
        if (quote == null) return;
        bool favorited = AppServices.Favorites.Toggle(quote);
        _vm.RefreshFavorite();
        ShowToast(favorited ? "已加入收藏" : "已取消收藏");
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e) => FavoriteCurrent();

    /// <summary>把当前句子渲染成分享图：存到 图片\拾句\ 并复制到剪贴板。</summary>
    public void ShareCurrent()
    {
        var quote = _vm.CurrentQuote;
        if (quote == null) return;
        try
        {
            var path = ShareCardService.SaveAndCopy(quote, AppServices.Settings);
            Log.Info("share card: " + path);
            ShowToast("分享图已复制，存在 图片\\拾句");
        }
        catch (Exception ex)
        {
            Log.Error("share card 生成失败", ex);
            ShowToast("分享图生成失败");
        }
    }

    private void OnShareClick(object sender, RoutedEventArgs e) => ShareCurrent();

    private void OnDailyClick(object sender, RoutedEventArgs e) => ShowDaily();

    private void CopyCurrent()
    {
        var quote = _vm.CurrentQuote;
        if (quote == null) return;
        try
        {
            Clipboard.SetText(quote.Display);
            ShowToast("已复制到剪贴板");
        }
        catch
        {
            ShowToast("复制失败");
        }
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ———————— 右键菜单 ————————

    private void OnWidgetMenuOpened(object sender, RoutedEventArgs e)
    {
        MenuFavItem.Header = _vm.IsFavorite ? "取消收藏" : "收藏当前句子";
        _autoTimer.Stop(); // 菜单展开期间不自动换句
    }

    private void OnWidgetMenuClosed(object sender, RoutedEventArgs e) => UpdateAutoTimer();

    private void OnMenuFavorites(object sender, RoutedEventArgs e) => App.Current.ShowFavorites();

    private void OnMenuSettings(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void OnMenuHide(object sender, RoutedEventArgs e) => Hide();

    private void OnMenuExit(object sender, RoutedEventArgs e) => App.Current.ExitApp();
}
