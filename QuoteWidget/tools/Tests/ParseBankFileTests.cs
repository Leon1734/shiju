using System.IO;
using QuoteWidget.Services;
using Xunit;

namespace QuoteWidget.Tests;

/// <summary>词库文件解析规则测试：规则变化时防止回归。</summary>
public class ParseBankFileTests : IDisposable
{
    private readonly string _tempDir;

    public ParseBankFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QuoteWidgetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private List<(string Text, string Source)> Parse(string content, string file = "bank.md")
    {
        var path = Path.Combine(_tempDir, file);
        File.WriteAllText(path, content);
        return QuoteRepository.ParseBankFile(path, "test")
            .Select(q => (q.Text, q.Source)).ToList();
    }

    [Fact]
    public void 单行句_出处取最后一个破折号之后()
    {
        var quotes = Parse("生活如此艰辛？——总是如此。——《这个杀手不太冷》");
        var q = Assert.Single(quotes);
        Assert.Equal("生活如此艰辛？——总是如此。", q.Text);
        Assert.Equal("《这个杀手不太冷》", q.Source);
    }

    [Fact]
    public void 编号行_每行独立成句()
    {
        var quotes = Parse("1. 忙着活，或忙着死。——《肖申克》\n2. 人生如逆旅，我亦是行人。——苏轼\n");
        Assert.Equal(2, quotes.Count);
        Assert.Equal("忙着活，或忙着死。", quotes[0].Text);
        Assert.Equal("《肖申克》", quotes[0].Source);
        Assert.Equal("人生如逆旅，我亦是行人。", quotes[1].Text);
    }

    [Fact]
    public void 缩进续行_英中两行合并为一句()
    {
        var quotes = Parse("1. Be yourself; everyone else is already taken.\n   做你自己，因为别人都已经有人做了。—— 王尔德\n");
        var q = Assert.Single(quotes);
        Assert.Equal("Be yourself; everyone else is already taken.\n做你自己，因为别人都已经有人做了。", q.Text);
        Assert.Equal("王尔德", q.Source);
    }

    [Fact]
    public void 空行_分隔两句()
    {
        var quotes = Parse("第一句\n\n第二句");
        Assert.Equal(2, quotes.Count);
        Assert.Equal("第一句", quotes[0].Text);
        Assert.Equal("第二句", quotes[1].Text);
    }

    [Fact]
    public void 每行一句_上一行以句末标点收尾则开新句()
    {
        var quotes = Parse("有志者，事竟成。\n永不言败。\ncontinue");
        Assert.Equal(3, quotes.Count);
    }

    [Fact]
    public void 连续无标点行_合并为一句()
    {
        var quotes = Parse("大河之水天上来\n奔流到海不复回");
        var q = Assert.Single(quotes);
        Assert.Equal("大河之水天上来\n奔流到海不复回", q.Text);
    }

    [Fact]
    public void 标题与分隔线_被忽略()
    {
        var quotes = Parse("# 分类标题\n---\n1. 真正的句子。——《出处》\n## 二级标题\n");
        var q = Assert.Single(quotes);
        Assert.Equal("真正的句子。", q.Text);
    }

    [Fact]
    public void 无出处句子_Source为空()
    {
        var quotes = Parse("只是一句话");
        var q = Assert.Single(quotes);
        Assert.Equal("", q.Source);
    }

    [Fact]
    public void 空文件_返回空列表()
    {
        Assert.Empty(Parse("\n\n# 只有标题\n"));
    }

    [Fact]
    public void 中文顿号编号_也识别为编号行()
    {
        var quotes = Parse("1、甲句\n2、乙句");
        Assert.Equal(2, quotes.Count);
        Assert.Equal("甲句", quotes[0].Text);
        Assert.Equal("乙句", quotes[1].Text);
    }

    [Fact]
    public void 内置词库文件名识别()
    {
        Assert.True(QuoteRepository.IsBuiltInBankFile(@"C:\x\词库\电影台词.md"));
        Assert.True(QuoteRepository.IsBuiltInBankFile(@"C:\x\词库\音乐歌词.md"));
        Assert.False(QuoteRepository.IsBuiltInBankFile(@"C:\x\词库\英文好句.md"));
        Assert.False(QuoteRepository.IsBuiltInBankFile(@"C:\x\词库\README.md"));
    }

    [Fact]
    public void 快捷键文本解析()
    {
        Assert.True(HotkeyService.TryParse("Ctrl+Alt+Q", out var mods, out var vk));
        Assert.Equal(HotkeyService.ModControl | HotkeyService.ModAlt, mods);
        Assert.Equal(0x51u, vk); // Q
        Assert.True(HotkeyService.TryParse("F9", out _, out var vkF9));
        Assert.Equal(0x78u, vkF9);
        Assert.True(HotkeyService.TryParse("Space", out _, out var vkSpace));
        Assert.Equal(0x20u, vkSpace);
        Assert.False(HotkeyService.TryParse("", out _, out _));
        Assert.False(HotkeyService.TryParse("Ctrl+笑", out _, out _));
    }
}
