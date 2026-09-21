using QuoteWidget.Services;
using Xunit;

namespace QuoteWidget.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("2.3.0", "2.2.0", true)]
    [InlineData("2.10.0", "2.9.9", true)]     // 数字比较而非字符串比较
    [InlineData("3.0.0", "2.99.99", true)]
    [InlineData("2.2.1", "2.2.0", true)]
    [InlineData("v2.3.0", "2.2.0", true)]     // 容忍 v 前缀
    [InlineData("2.2.0", "2.2.0", false)]     // 相同不算新
    [InlineData("2.1.9", "2.2.0", false)]
    [InlineData("1.9.9", "2.0.0", false)]
    [InlineData("2.2", "2.2.0", false)]       // 缺段按 0
    [InlineData("2.3", "2.2.9", true)]
    public void 版本比较(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(latest, Version.Parse(current)));
    }

    [Theory]
    [InlineData("2.3.0", true)]
    [InlineData("v3.1", true)]
    [InlineData("2", false)]        // 至少两段
    [InlineData("abc", false)]
    [InlineData("2.3.beta", false)] // 非数字段
    [InlineData("", false)]
    public void 版本解析(string text, bool shouldParse)
    {
        Assert.Equal(shouldParse, UpdateChecker.TryParseVersion(text, out _));
    }
}
