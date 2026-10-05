using System.Windows;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class ShutdownDialog : Window
{
    private bool _subscribed;

    public ShutdownDialog()
    {
        InitializeComponent();

        ActionCombo.ItemsSource = new List<(string Value, string Label)>
        {
            ("Shutdown", "关机"),
            ("Restart", "重启"),
            ("Sleep", "睡眠（无法由系统延时，到点直接进入）")
        };
        ActionCombo.DisplayMemberPath = "Label";
        ActionCombo.SelectedValuePath = "Value";
        ActionCombo.SelectedValue = AppServices.Settings.ShutdownAction is { Length: > 0 } s ? s : "Shutdown";

        HintText.Text = "关机/重启在最后 60 秒会交给 Windows 系统倒计时（有系统通知，可随时取消）；" +
                        "计划会保存在本地，程序重启后依然生效。";
        RefreshStatus();
        ShutdownService.StateChanged += RefreshStatus;
        _subscribed = true;
    }

    private void RefreshStatus()
    {
        StatusText.Text = ShutdownService.IsScheduled
            ? $"当前计划：{ShutdownService.RemainingText}后{ShutdownService.ActionName(ShutdownService.Action)}"
            : "当前没有定时计划。";
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
        RefreshStatus();
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
