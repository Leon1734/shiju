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
