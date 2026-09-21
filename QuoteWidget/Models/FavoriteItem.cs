namespace QuoteWidget.Models;

public class FavoriteItem
{
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<string> Tags { get; set; } = new();

    public string CategoryName => Categories.NameOf(Category);
    public string SavedAtText => SavedAt.ToString("yyyy-MM-dd HH:mm");
    public string TagsText => string.Join(" / ", Tags);
    public string TagsDisplay => Tags.Count == 0 ? "" : "🏷 " + TagsText;
}
