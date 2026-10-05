using System.IO;
using QuoteWidget.Services;
using Xunit;

namespace QuoteWidget.Tests;

public class FeatureTests : IDisposable
{
    private readonly string _tempDir;

    public FeatureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QuoteWidgetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ———————— 词库体检 ————————

    private List<QuoteRepository.BankDiagnostic> Analyze(string content)
    {
        var path = Path.Combine(_tempDir, "bank.md");
        File.WriteAllText(path, content);
        return QuoteRepository.AnalyzeBankFile(path);
    }

    [Fact]
    public void 体检_缩进译文缺少主句()
    {
        var issues = Analyze("# 标题\n\n   孤零零的译文行\n");
        Assert.Contains(issues, i => i.Issue.Contains("缺少主句"));
    }

    [Fact]
    public void 体检_多行句编号缺译文()
    {
        var issues = Analyze("1. English line.\n2. Another English.\n   有译文。\n");
        Assert.Contains(issues, i => i.Issue.Contains("缺少译文行"));
    }

    [Fact]
    public void 体检_编号后没有内容()
    {
        var issues = Analyze("1.\n2. 有内容。——出处\n");
        Assert.Contains(issues, i => i.Issue.Contains("编号后没有内容"));
    }

    [Fact]
    public void 体检_行内容重复()
    {
        var issues = Analyze("1. 同一句。——甲\n2. 同一句。——乙\n");
        Assert.Contains(issues, i => i.Issue.Contains("重复"));
    }

    [Fact]
    public void 体检_干净的词库无问题()
    {
        var issues = Analyze("1. Be yourself.\n   做你自己。—— 王尔德\n\n2. 忙着活。——《肖申克》\n");
        Assert.Empty(issues);
    }

    // ———————— 词库时段 ————————

    [Fact]
    public void 时段_全天始终生效()
    {
        Assert.True(QuoteRepository.IsScheduleActive(0, 3));
        Assert.True(QuoteRepository.IsScheduleActive(0, 15));
    }

    [Fact]
    public void 时段_白天8到22点()
    {
        Assert.False(QuoteRepository.IsScheduleActive(1, 7));
        Assert.True(QuoteRepository.IsScheduleActive(1, 8));
        Assert.True(QuoteRepository.IsScheduleActive(1, 21));
        Assert.False(QuoteRepository.IsScheduleActive(1, 22));
    }

    [Fact]
    public void 时段_夜间22到8点_跨零点()
    {
        Assert.True(QuoteRepository.IsScheduleActive(2, 23));
        Assert.True(QuoteRepository.IsScheduleActive(2, 3));
        Assert.False(QuoteRepository.IsScheduleActive(2, 12));
        Assert.True(QuoteRepository.IsScheduleActive(2, 0));
    }

    // ———————— 定时关机 ————————

    [Theory]
    [InlineData(2, 5, 0, "2 小时 5 分钟")]
    [InlineData(0, 30, 0, "30 分钟")]
    [InlineData(0, 1, 30, "1 分钟")]
    [InlineData(0, 0, 45, "45 秒")]
    public void 剩余时间文案(int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, ShutdownService.FormatRemaining(new TimeSpan(hours, minutes, seconds)));
    }

    [Fact]
    public void 剩余时间为负显示即将()
    {
        Assert.Equal("即将", ShutdownService.FormatRemaining(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void 时刻解析_支持中文冒号与顺延()
    {
        Assert.True(ShutdownService.TryParseClock("23:30", out var at1));
        Assert.Equal(23, at1.Hour);
        Assert.Equal(30, at1.Minute);
        Assert.True(at1 > DateTime.Now);

        Assert.True(ShutdownService.TryParseClock("00：05", out var at2)); // 中文冒号
        Assert.Equal(5, at2.Minute);

        Assert.True(ShutdownService.TryParseClock("00:01", out var at3));  // 今天已过 → 明天
        Assert.Equal(DateTime.Now.Date.AddDays(1).AddMinutes(1), at3);

        Assert.False(ShutdownService.TryParseClock("25:00", out _));
        Assert.False(ShutdownService.TryParseClock("abc", out _));
    }

    // ———————— 节日识别 ————————

    [Fact]
    public void 节日_公历节日()
    {
        Assert.Equal("元旦", HolidayService.GetToday(new DateTime(2027, 1, 1)));
        Assert.Equal("国庆节", HolidayService.GetToday(new DateTime(2027, 10, 1)));
        Assert.Equal("圣诞节", HolidayService.GetToday(new DateTime(2027, 12, 25)));
    }

    [Fact]
    public void 节日_农历2025春节()
    {
        Assert.Equal("春节", HolidayService.GetToday(new DateTime(2025, 1, 29)));
        Assert.Equal("中秋节", HolidayService.GetToday(new DateTime(2025, 10, 6)));
    }

    [Fact]
    public void 节日_非节日返回null()
    {
        Assert.Null(HolidayService.GetToday(new DateTime(2027, 6, 15)));
        // 2027 年春节不在内置农历表内 → 当天识别不到（表外年份只认公历节日）
        Assert.Null(HolidayService.GetToday(new DateTime(2027, 2, 6)));
    }
}
