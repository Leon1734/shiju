using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuoteWidget.Models;
using System.Linq;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class TranslateWindow : Window
{
    private sealed record EngineItem(string Kind, string Label);

    private bool _busy;

    public TranslateWindow()
    {
        InitializeComponent();
        var engines = LlmEngine.Presets
            .Select(p => new EngineItem(p.Kind, p.Name)).ToList();        EngineCombo.ItemsSource = engines;
        EngineCombo.DisplayMemberPath = nameof(EngineItem.Label);
        EngineCombo.SelectedValuePath = nameof(EngineItem.Kind);
        EngineCombo.SelectedValue = AppServices.Settings.TranslateEngine;
        RestoreBounds();
        UpdateStatus();
        InputBox.Focus();
    }

    private void UpdateStatus()
    {
        if (EngineCombo.SelectedValue as string != "OfflineDict")
        {
            StatusText.Text = "大模型引擎需要 API Key：在 设置 → 翻译引擎 里填写。Key 只保存在本机。";
            return;
        }
        var (count, exists) = OfflineDictEngine.Status();
        StatusText.Text = exists
            ? $"离线词典已加载 {count} 条（程序目录 词典\\ecdict-compact.txt，可自行补充词条）。短句查词，长句逐词直译。"
            : "未找到离线词典：请把 ecdict-compact.txt 放入程序目录的 词典\\ 文件夹。";
    }

    /// <summary>带入待翻译文本并自动执行翻译（供挂件"翻译小窗"入口调用）。</summary>
    public void Prefill(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        InputBox.Text = text;
        OutputBox.Clear();
        OnTranslate(this, new RoutedEventArgs());
    }

    private async void OnTranslate(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (_busy) return;
        _busy = true;
        OutputBox.Text = "";
        StatusText.Text = "翻译中…";
        try
        {
            if (EngineCombo.SelectedValue is string kind) AppServices.Settings.TranslateEngine = kind;
            var result = await TranslationService.TranslateAsync(text);
            OutputBox.Text = result.Success ? result.Output : "";
            StatusText.Text = result.Success ? "完成。" : result.Error ?? "翻译失败";
            if (!result.Success) Log.Warn("translate: " + result.Error);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private void OnCopyResult(object sender, RoutedEventArgs e)
    {
        if (OutputBox.Text.Length == 0) return;
        try { Clipboard.SetText(OutputBox.Text); StatusText.Text = "已复制到剪贴板。"; } catch { }
    }

    /// <summary>恢复上次的窗口位置与大小（首次打开则保持默认居中）。</summary>
    private void RestoreBounds()
    {
        var bounds = AppServices.Settings.TranslateBounds;
        if (bounds is not { Length: 4 } || bounds[2] < 380 || bounds[3] < 300) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds[0];
        Top = bounds[1];
        Width = bounds[2];
        Height = bounds[3];

        // 防止显示器变化后窗口跑到屏幕外
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, vLeft + 4, Math.Max(vLeft + 4, vLeft + vw - Width - 4));
        Top = Math.Clamp(Top, vTop + 4, Math.Max(vTop + 4, vTop + vh - Height - 4));
    }

    /// <summary>关闭时保存窗口位置与大小。</summary>
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (WindowState == WindowState.Normal)
                AppServices.Settings.TranslateBounds = new[] { Left, Top, Width, Height };
            SettingsStore.Save(AppServices.Settings);
        }
        catch { }
        base.OnClosed(e);
    }

    private void OnOpenApiSettings(object sender, RoutedEventArgs e)
        => App.Current.ShowSettings(focusTranslation: true);

    private void OnClear(object sender, RoutedEventArgs e)
    {
        InputBox.Clear();
        OutputBox.Clear();
        InputBox.Focus();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            OnTranslate(sender, e);
        }
    }
}
