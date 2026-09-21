namespace QuoteWidget.Models;

public class Quote
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string Category { get; set; } = "";

    public string CategoryName => Categories.NameOf(Category);

    /// <summary>复制到剪贴板用的完整文本。</summary>
    public string Display => string.IsNullOrEmpty(Source) ? Text : $"{Text}\n—— {Source}";
}

public static class Categories
{
    public static readonly string[] All = { "movie", "game", "novel", "lyric" };

    public static string NameOf(string category)
    {
        if (category.StartsWith("custom:", StringComparison.Ordinal))
            return category["custom:".Length..]; // 自定义词库：分类名即文件名
        return category switch
        {
            "movie" => "电影台词",
            "game" => "游戏台词",
            "novel" => "小说名句",
            "lyric" => "音乐歌词",
            "hitokoto" => "一言",
            _ => "语录"
        };
    }
}

/// <summary>quotes.json 的根结构。</summary>
public class QuotesFile
{
    public int Version { get; set; }
    public List<Quote> Quotes { get; set; } = new();
}
