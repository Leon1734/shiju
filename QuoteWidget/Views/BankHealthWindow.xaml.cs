using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class BankHealthWindow : Window
{
    public BankHealthWindow()
    {
        InitializeComponent();
        var issues = ScanAllBanks();
        SummaryText.Text = issues.Count == 0
            ? $"共检查 {CountBankFiles()} 个词库文件，未发现问题。"
            : $"共检查 {CountBankFiles()} 个词库文件，发现 {issues.Count} 个问题。双击问题条目可用记事本打开对应文件（行号见描述）。";
        IssueList.ItemsSource = issues.Select(d => $"[{d.File}:{d.Line}] {d.Issue}").ToList();
        EmptyText.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IssueList.Visibility = issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Log.Info($"bank health: {issues.Count} issue(s)");
    }

    private static int CountBankFiles()
    {
        if (!Directory.Exists(QuoteRepository.BankDir)) return 0;
        return Directory.EnumerateFiles(QuoteRepository.BankDir)
            .Count(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    private static List<QuoteRepository.BankDiagnostic> ScanAllBanks()
    {
        var issues = new List<QuoteRepository.BankDiagnostic>();
        if (!Directory.Exists(QuoteRepository.BankDir)) return issues;
        foreach (var file in Directory.EnumerateFiles(QuoteRepository.BankDir)
                     .Where(f => (f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                               || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                              && !Path.GetFileName(f).StartsWith("README", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.CurrentCulture))
        {
            issues.AddRange(QuoteRepository.AnalyzeBankFile(file));
        }
        return issues;
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is not string text) return;
        // 条目格式 "[文件:行] 问题"
        var file = QuoteRepository.BankDir;
        try
        {
            var start = text.IndexOf('[') + 1;
            var end = text.IndexOf(':', start);
            var fileName = text[start..end];
            var fullPath = Path.Combine(QuoteRepository.BankDir, fileName);
            if (File.Exists(fullPath))
                Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{fullPath}\"", UseShellExecute = true });
        }
        catch { }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(QuoteRepository.BankDir);
            Process.Start(new ProcessStartInfo { FileName = QuoteRepository.BankDir, UseShellExecute = true });
        }
        catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
