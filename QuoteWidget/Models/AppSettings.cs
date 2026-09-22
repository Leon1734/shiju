using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace QuoteWidget.Models;

public enum BackgroundMode
{
    /// <summary>全透明：只显示文字（带轻微阴影保证可读性）。</summary>
    Transparent,
    /// <summary>纯色卡片：不透明度跟随 BgOpacity。</summary>
    Solid,
    /// <summary>半透明卡片：不透明度打 45 折，若隐若现。</summary>
    Translucent
}

/// <summary>
/// 全部应用设置。实现 INPC：设置窗口修改后，挂件与托盘实时感知并应用。
/// </summary>
public class AppSettings : INotifyPropertyChanged
{
    private BackgroundMode _bgMode = BackgroundMode.Transparent;
    private string _bgColor = "#2D2D3A";
    private double _bgOpacity = 0.55;
    private double _cornerRadius = 16;
    private double _widgetWidth = 420;
    private string _textColor = "#FFFFFF";
    private string _fontFamily = "Microsoft YaHei UI";
    private double _fontSize = 16;
    private bool _bold;

    private bool _autoSwitch = true;
    private int _autoSwitchSeconds = 30;
    private bool _randomMode = true;
    private bool _pauseOnHover = true;
    private bool _topmost = true;

    private bool _catMovie = true;
    private bool _catGame = true;
    private bool _catNovel = true;
    private bool _catLyric = true;
    private bool _useHitokoto = true;

    private bool _clickThrough;
    private bool _autoStart;

    private bool _typewriter = true;
    private int _settingsVersion;
    private Dictionary<string, bool> _customCategories = new();

    private bool _adaptiveTextColor = true;
    private bool _dailyMode;
    private bool _dndEnabled;
    private int _dndStartHour = 23;
    private int _dndEndHour = 7;

    private string _hotkeySwitch = "Ctrl+Alt+Q";
    private string _hotkeyFavorite = "Ctrl+Alt+F";
    private string _hotkeyToggle = "Ctrl+Alt+H";

    private bool _holidayEgg = true;
    private Dictionary<string, int> _bankSchedules = new();

    private string _translateEngine = "Zhipu";
    private string _zhipuApiKey = "在这里填入你的智谱APIKey";
    private string _siliconApiKey = "";
    private string _siliconModel = "Qwen/Qwen2.5-7B-Instruct";
    private string _deepseekApiKey = "";
    private string _customBaseUrl = "";
    private string _customApiKey = "";
    private string _customModel = "";

    private Dictionary<string, string> _engineUrls = new();
    private Dictionary<string, string> _engineModels = new();

    private string _qwenApiKey = "";
    private string _moonshotApiKey = "";
    private string _sparkApiKey = "";
    private string _ollamaModel = "qwen2.5:3b";

    private int _autostartDelaySeconds;
    private bool _foldMultiLine;
    private double _lineSpacing = 1.35;
    private bool _gradientText;
    private bool _rainbowText;
    private string _updateUrl = "";

    private bool _autoHideFullscreen = true;
    private bool _acrylicBackdrop;
    private bool _onboardingShown;
    private Dictionary<string, double[]> _windowPositions = new();

    private double? _windowLeft;
    private double? _windowTop;
    private string _lastQuoteText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    // —— 外观 ——
    public BackgroundMode BgMode { get => _bgMode; set => Set(ref _bgMode, value); }
    public string BgColor { get => _bgColor; set => Set(ref _bgColor, value); }
    public double BgOpacity { get => _bgOpacity; set => Set(ref _bgOpacity, Math.Clamp(value, 0.05, 1)); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, Math.Clamp(value, 0, 32)); }
    public double WidgetWidth { get => _widgetWidth; set => Set(ref _widgetWidth, Math.Clamp(value, 280, 720)); }
    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Clamp(value, 11, 32)); }
    public bool Bold { get => _bold; set => Set(ref _bold, value); }

    // —— 行为 ——
    public bool AutoSwitch { get => _autoSwitch; set => Set(ref _autoSwitch, value); }
    public int AutoSwitchSeconds { get => _autoSwitchSeconds; set => Set(ref _autoSwitchSeconds, Math.Clamp(value, 5, 600)); }
    public bool RandomMode { get => _randomMode; set => Set(ref _randomMode, value); }
    public bool PauseOnHover { get => _pauseOnHover; set => Set(ref _pauseOnHover, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }

    // —— 内容 ——
    public bool CatMovie { get => _catMovie; set => Set(ref _catMovie, value); }
    public bool CatGame { get => _catGame; set => Set(ref _catGame, value); }
    public bool CatNovel { get => _catNovel; set => Set(ref _catNovel, value); }
    public bool CatLyric { get => _catLyric; set => Set(ref _catLyric, value); }
    public bool UseHitokoto { get => _useHitokoto; set => Set(ref _useHitokoto, value); }

    // —— 通用 ——
    public bool ClickThrough { get => _clickThrough; set => Set(ref _clickThrough, value); }
    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }

    // —— 内容扩展 ——
    /// <summary>换句时打字机逐字显示。</summary>
    public bool Typewriter { get => _typewriter; set => Set(ref _typewriter, value); }

    /// <summary>设置文件版本号，用于旧配置一次性迁移。</summary>
    public int SettingsVersion { get => _settingsVersion; set => Set(ref _settingsVersion, value); }

    /// <summary>自定义离线词库（文件名 -> 是否启用），文件放在程序目录 词库\ 文件夹。</summary>
    public Dictionary<string, bool> CustomCategories { get => _customCategories; set => Set(ref _customCategories, value); }

    // —— v2 ——

    /// <summary>全透明模式下按壁纸亮度自动选黑字/白字。</summary>
    public bool AdaptiveTextColor { get => _adaptiveTextColor; set => Set(ref _adaptiveTextColor, value); }

    /// <summary>每日一句：每天固定一句，跨零点自动更换（手动换句仍可用）。</summary>
    public bool DailyMode { get => _dailyMode; set => Set(ref _dailyMode, value); }

    /// <summary>勿扰时段：时段内暂停自动换句。</summary>
    public bool DndEnabled { get => _dndEnabled; set => Set(ref _dndEnabled, value); }
    public int DndStartHour { get => _dndStartHour; set => Set(ref _dndStartHour, Math.Clamp(value, 0, 23)); }
    public int DndEndHour { get => _dndEndHour; set => Set(ref _dndEndHour, Math.Clamp(value, 0, 23)); }

    /// <summary>全局快捷键（如 "Ctrl+Alt+Q"，空 = 停用）。</summary>
    public string HotkeySwitch { get => _hotkeySwitch; set => Set(ref _hotkeySwitch, value); }
    public string HotkeyFavorite { get => _hotkeyFavorite; set => Set(ref _hotkeyFavorite, value); }
    public string HotkeyToggle { get => _hotkeyToggle; set => Set(ref _hotkeyToggle, value); }

    /// <summary>挂件窗口位置（null = 首次启动，用默认位置）。</summary>
    public double? WindowLeft { get => _windowLeft; set => Set(ref _windowLeft, value); }
    public double? WindowTop { get => _windowTop; set => Set(ref _windowTop, value); }

    /// <summary>上次运行最后显示的句子，用于下次启动时避开（仅退出时落盘一次）。</summary>
    public string LastQuoteText { get => _lastQuoteText; set => Set(ref _lastQuoteText, value); }

    // —— v2.1 内容扩展 ——

    /// <summary>节日彩蛋：节日当天优先展示节日祝福包的句子并显示角标。</summary>
    public bool HolidayEgg { get => _holidayEgg; set => Set(ref _holidayEgg, value); }

    /// <summary>词库生效时段（词库名 -> 0全天 / 1白天8-22点 / 2夜间22-8点）。</summary>
    public Dictionary<string, int> BankSchedules { get => _bankSchedules; set => Set(ref _bankSchedules, value); }

    // —— v2.2 翻译 ——

    /// <summary>翻译引擎：OfflineDict / Zhipu / SiliconFlow / DeepSeek / Custom。</summary>
    public string TranslateEngine { get => _translateEngine; set => Set(ref _translateEngine, value); }
    public string ZhipuApiKey { get => _zhipuApiKey; set => Set(ref _zhipuApiKey, value); }
    public string SiliconApiKey { get => _siliconApiKey; set => Set(ref _siliconApiKey, value); }
    public string SiliconModel { get => _siliconModel; set => Set(ref _siliconModel, value); }
    public string DeepseekApiKey { get => _deepseekApiKey; set => Set(ref _deepseekApiKey, value); }
    public string CustomBaseUrl { get => _customBaseUrl; set => Set(ref _customBaseUrl, value); }
    public string CustomApiKey { get => _customApiKey; set => Set(ref _customApiKey, value); }
    public string CustomModel { get => _customModel; set => Set(ref _customModel, value); }
    public string QwenApiKey { get => _qwenApiKey; set => Set(ref _qwenApiKey, value); }
    public string MoonshotApiKey { get => _moonshotApiKey; set => Set(ref _moonshotApiKey, value); }
    public string SparkApiKey { get => _sparkApiKey; set => Set(ref _sparkApiKey, value); }
    public string OllamaModel { get => _ollamaModel; set => Set(ref _ollamaModel, value); }

    /// <summary>各引擎自定义请求地址（kind -> url；空 = 用预设默认地址）。</summary>
    public Dictionary<string, string> EngineUrls { get => _engineUrls; set => Set(ref _engineUrls, value); }
    /// <summary>各引擎自定义模型名（kind -> model；空 = 用预设默认模型）。</summary>
    public Dictionary<string, string> EngineModels { get => _engineModels; set => Set(ref _engineModels, value); }

    // —— v2.3 ——

    /// <summary>开机自启延迟秒数（0/15/30/60），错开开机高峰。</summary>
    public int AutostartDelaySeconds { get => _autostartDelaySeconds; set => Set(ref _autostartDelaySeconds, value); }

    /// <summary>多行句（如英中对照）只显示第一行，悬停展开全文。</summary>
    public bool FoldMultiLine { get => _foldMultiLine; set => Set(ref _foldMultiLine, value); }

    /// <summary>正文行距倍数（字号 × 倍数）。</summary>
    public double LineSpacing { get => _lineSpacing; set => Set(ref _lineSpacing, Math.Clamp(value, 1.0, 2.0)); }

    /// <summary>文字渐变色（主色向亮/暗延伸的纵向渐变）。</summary>
    public bool GradientText { get => _gradientText; set => Set(ref _gradientText, value); }

    /// <summary>七彩文字：彩虹渐变色的正文。</summary>
    public bool RainbowText { get => _rainbowText; set => Set(ref _rainbowText, value); }

    /// <summary>更新检查地址（JSON：version/url/notes），空 = 停用。</summary>
    public string UpdateUrl { get => _updateUrl; set => Set(ref _updateUrl, value); }

    /// <summary>全屏应用（游戏/视频/演示）运行时自动隐藏挂件，退出全屏自动恢复。</summary>
    public bool AutoHideFullscreen { get => _autoHideFullscreen; set => Set(ref _autoHideFullscreen, value); }

    /// <summary>卡片模式使用亚克力模糊背景（Windows 11；切换后自动重建窗口）。</summary>
    public bool AcrylicBackdrop { get => _acrylicBackdrop; set => Set(ref _acrylicBackdrop, value); }

    /// <summary>首启引导是否已展示过。</summary>
    public bool OnboardingShown { get => _onboardingShown; set => Set(ref _onboardingShown, value); }

    /// <summary>按显示器记忆的窗口位置（显示器设备名 -> [Left, Top]，DIP）。</summary>
    public Dictionary<string, double[]> WindowPositions { get => _windowPositions; set => Set(ref _windowPositions, value); }

    /// <summary>恢复默认外观与行为，保留窗口位置。</summary>
    public void ResetToDefaults()
    {
        BgMode = BackgroundMode.Transparent;
        BgColor = "#2D2D3A";
        BgOpacity = 0.55;
        CornerRadius = 16;
        WidgetWidth = 420;
        TextColor = "#FFFFFF";
        FontFamily = "Microsoft YaHei UI";
        FontSize = 16;
        Bold = false;

        AutoSwitch = true;
        AutoSwitchSeconds = 30;
        RandomMode = true;
        PauseOnHover = true;
        Topmost = true;

        CatMovie = true;
        CatGame = true;
        CatNovel = true;
        CatLyric = true;
        UseHitokoto = true;

        ClickThrough = false;
        AutoStart = false;
        Typewriter = true;
        SettingsVersion = 3;

        AdaptiveTextColor = true;
        DailyMode = false;
        DndEnabled = false;
        DndStartHour = 23;
        DndEndHour = 7;
        HotkeySwitch = "Ctrl+Alt+Q";
        HotkeyFavorite = "Ctrl+Alt+F";
        HotkeyToggle = "Ctrl+Alt+H";

        HolidayEgg = true;
        TranslateEngine = "Zhipu";
        EngineUrls.Clear();
        EngineModels.Clear();
        ZhipuApiKey = "在这里填入你的智谱APIKey";

        AutostartDelaySeconds = 0;
        FoldMultiLine = false;
        LineSpacing = 1.35;
        GradientText = false;
        RainbowText = false;
        UpdateUrl = "";
        AutoHideFullscreen = true;
        AcrylicBackdrop = false;
    }
}
