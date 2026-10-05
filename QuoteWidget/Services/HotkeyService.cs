using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>
/// 全局快捷键：RegisterHotKey + 挂件窗口消息钩子。
/// 动作编号：1=换句 2=收藏当前句 3=显示/隐藏挂件。
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;
    private const int WmHotkey = 0x0312;

    public enum Action { Switch = 1, Favorite = 2, Toggle = 3, Pause = 4 }

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly List<int> _registered = new();

    /// <summary>快捷键触发（应用内，UI 线程）。</summary>
    public event Action<Action>? Pressed;

    /// <summary>挂件窗口句柄就绪后调用一次。</summary>
    public void Attach(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            handled = true;
            var action = (Action)wParam.ToInt32();
            System.Windows.Application.Current?.Dispatcher.Invoke(() => Pressed?.Invoke(action));
        }
        return IntPtr.Zero;
    }

    /// <summary>各动作的备用组合（首选被占用时按顺序尝试）。</summary>
    private static readonly Dictionary<Action, string[]> Fallbacks = new()
    {
        [Action.Switch] = new[] { "Ctrl+Alt+Q", "Ctrl+Alt+Right", "Ctrl+Alt+1" },
        [Action.Favorite] = new[] { "Ctrl+Alt+F", "Ctrl+Alt+2" },
        [Action.Toggle] = new[] { "Ctrl+Alt+H", "Ctrl+Alt+3" },
        [Action.Pause] = new[] { "Ctrl+Alt+P", "Ctrl+Alt+Space", "Ctrl+Alt+M", "Ctrl+Alt+9" },
    };

    /// <summary>按设置重注册全部快捷键；返回被自动改用的组合（动作 -> 实际生效组合）。
    /// 当配置的组合被其他程序占用时，自动尝试备用组合并回报，避免功能静默失效。</summary>
    public Dictionary<Action, string> Apply(AppSettings settings)
    {
        UnregisterAll();
        var changed = new Dictionary<Action, string>();
        ApplyOne(Action.Switch, settings.HotkeySwitch, changed);
        ApplyOne(Action.Favorite, settings.HotkeyFavorite, changed);
        ApplyOne(Action.Toggle, settings.HotkeyToggle, changed);
        ApplyOne(Action.Pause, settings.HotkeyPause, changed);
        return changed;
    }

    private void ApplyOne(Action action, string? configured, Dictionary<Action, string> changed)
    {
        foreach (var combo in new[] { configured }.Concat(Fallbacks[action])
                     .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            if (TryRegister((int)action, combo))
            {
                if (combo != configured) changed[action] = combo!;
                return;
            }
        }
        Log.Warn($"hotkey: {action} 的全部候选组合都注册失败");
    }

    private bool TryRegister(int id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!TryParse(text, out uint mods, out uint vk))
        {
            Log.Warn($"hotkey: 无法识别「{text}」，已跳过");
            return false;
        }
        if (RegisterHotKey(_hwnd, id, mods, vk))
        {
            _registered.Add(id);
            Log.Info($"hotkey: 已注册「{text}」-> 动作{id}");
            return true;
        }
        Log.Warn($"hotkey: 「{text}」注册失败（可能被其他程序占用）");
        return false;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    /// <summary>解析 "Ctrl+Alt+Q" 风格文本为修饰键 + 虚拟键码；支持字母/数字/F1~F24/方向键/空格。</summary>
    public static bool TryParse(string? text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (tokens.Count == 0) return false;

        foreach (var token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win" or "windows": mods |= ModWin; break;
                default: return false;
            }
        }

        var key = tokens[^1].ToLowerInvariant();
        if (key.Length == 1 && key[0] is >= 'a' and <= 'z')
            vk = (uint)(0x41 + (key[0] - 'a'));
        else if (key.Length == 2 && key[0] == 'd' && char.IsAsciiDigit(key[1]))
            vk = (uint)(0x30 + (key[1] - '0'));
        else if (key.Length >= 2 && key[0] == 'f' && int.TryParse(key[1..], out var fn) && fn is >= 1 and <= 24)
            vk = (uint)(0x70 + fn - 1);
        else if (key == "space") vk = 0x20;
        else if (key == "up") vk = 0x26;
        else if (key == "down") vk = 0x28;
        else if (key == "left") vk = 0x25;
        else if (key == "right") vk = 0x27;
        else return false;

        return true;
    }

    public void Dispose() => UnregisterAll();
}
