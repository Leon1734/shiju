using System.Windows;
using System.Windows.Controls;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class ShutdownDialog : Window
{
    /// <summary>下拉项必须用 record（有属性）：元组字段 WPF 绑定读不到会显示空白（已踩坑三次）。</summary>
    private sealed record ActionItem(string Value, string Label);

    private bool _subscribed;
    private bool _initializing = true;

    public ShutdownDialog()
    {
        InitializeComponent();

        ActionCombo.ItemsSource = new List<ActionItem>
        {
            new("Shutdown", "关机"),
            new("Restart", "重启"),
            new("Sleep", "睡眠（到点直接进入）")
        };
        ActionCombo.DisplayMemberPath = nameof(ActionItem.Label);
        ActionCombo.SelectedValuePath = nameof(ActionItem.Value);
        ActionCombo.SelectedValue = AppServices.Settings.ShutdownAction is { Length: > 0 } s ? s : "Shutdown";

        // 恢复上次的输入
        MinutesBox.Text = AppServices.Settings.ShutdownLastMinutes.ToString();
        ClockBox.Text = AppServices.Settings.ShutdownLastClock;
        if (AppServices.Settings.ShutdownUseClock) ClockRadio.IsChecked = true;
        else CountdownRadio.IsChecked = true;

        // 快捷预设按钮
        foreach (var minutes in new[] { 15, 30, 60, 120 })
        {
            var button = new Button
            {
                Content = $"{minutes} 分钟",
                Style = TryFindResource("DialogButton") as Style,
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = minutes
            };
            button.Click += OnPresetClick;
            PresetPanel.Children.Add(button);
        }

        ShutdownService.StateChanged += RefreshStatus;
        _subscribed = true;

        _initializing = false;
        OnModeChanged(null, null);
        RefreshStatus();
    }

    // ———————— 交互联动 ————————

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int minutes }) return;
        MinutesBox.Text = minutes.ToString();
        CountdownRadio.IsChecked = true;
        ScheduleNow(minutes);
    }

    private void OnModeChanged(object? sender, RoutedEventArgs? e)
    {
        if (_initializing) return;
        bool countdown = CountdownRadio.IsChecked == true;
        MinutesBox.IsEnabled = countdown;
        ClockBox.IsEnabled = !countdown;
        UpdatePreview();
    }

    private void OnInputChanged(object? sender, RoutedEventArgs? e)
    {
        if (_initializing) return;
        UpdatePreview();
    }

    /// <summary>实时预览：将于哪个具体时间执行什么动作。</summary>
    private void UpdatePreview()
    {
        var action = Enum.TryParse<ShutdownAction>(ActionCombo.SelectedValue as string, out var a)
            ? a : ShutdownAction.Shutdown;
        var actionName = ShutdownService.ActionName(action);

        if (CountdownRadio.IsChecked == true)
        {
            if (!int.TryParse(MinutesBox.Text.Trim(), out int minutes) || minutes is < 1 or > 1440)
            {
                PreviewText.Text = "请输入 1 - 1440 之间的分钟数";
                return;
            }
            var at = DateTime.Now.AddMinutes(minutes);
            PreviewText.Text = $"将于 {DescribeTime(at)}（约 {minutes} 分钟后）{actionName}";
        }
        else
        {
            if (!ShutdownService.TryParseClock(ClockBox.Text, out var at))
            {
                PreviewText.Text = "时刻格式不正确，请按 HH:mm 输入，例如 23:30";
                return;
            }
            PreviewText.Text = $"将于 {DescribeTime(at)}{actionName}";
        }
    }

    private static string DescribeTime(DateTime at)
    {
        var today = DateTime.Now.Date;
        string day = at.Date == today ? "今天" : at.Date == today.AddDays(1) ? "明天" : at.ToString("M 月 d 日");
        return $"{day} {at:HH:mm}";
    }

    private void RefreshStatus()
    {
        StatusText.Text = ShutdownService.IsScheduled
            ? $"当前计划：{ShutdownService.RemainingText}后{ShutdownService.ActionName(ShutdownService.Action)}"
            : "当前没有定时计划。";
    }

    // ———————— 设定 / 取消 ————————

    private void ScheduleNow(int minutes)
    {
        var action = Enum.TryParse<ShutdownAction>(ActionCombo.SelectedValue as string, out var a)
            ? a : ShutdownAction.Shutdown;
        ShutdownService.Schedule(DateTime.Now.AddMinutes(minutes), action);
        RememberInputs();
        RefreshStatus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var action = Enum.TryParse<ShutdownAction>(ActionCombo.SelectedValue as string, out var a)
            ? a : ShutdownAction.Shutdown;

        DateTime at;
        if (ClockRadio.IsChecked == true)
        {
            if (!ShutdownService.TryParseClock(ClockBox.Text, out at))
            {
                MessageBox.Show("时刻格式不正确，请按 HH:mm 输入，例如 23:30。", "拾句",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (!int.TryParse(MinutesBox.Text.Trim(), out int minutes) || minutes is < 1 or > 1440)
            {
                MessageBox.Show("请输入 1 - 1440 之间的分钟数。", "拾句",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            at = DateTime.Now.AddMinutes(minutes);
        }

        ShutdownService.Schedule(at, action);
        RememberInputs();
        RefreshStatus();
    }

    /// <summary>记住本次输入，下次打开时恢复。</summary>
    private void RememberInputs()
    {
        var s = AppServices.Settings;
        if (int.TryParse(MinutesBox.Text.Trim(), out int minutes)) s.ShutdownLastMinutes = minutes;
        s.ShutdownLastClock = ClockBox.Text.Trim();
        s.ShutdownUseClock = ClockRadio.IsChecked == true;
        SettingsStore.Save(s);
    }

    private void OnCancelPlan(object sender, RoutedEventArgs e)
    {
        ShutdownService.Cancel();
        RefreshStatus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_subscribed) ShutdownService.StateChanged -= RefreshStatus;
        base.OnClosed(e);
    }
}
