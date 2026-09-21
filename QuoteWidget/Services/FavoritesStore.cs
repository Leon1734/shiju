using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>收藏夹：内存列表 + %APPDATA% 持久化，任何增删自动落盘。</summary>
public class FavoritesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly string _filePath = Path.Combine(SettingsStore.DataDir, "favorites.json");

    public ObservableCollection<FavoriteItem> Items { get; } = new();

    /// <summary>任何增删后触发，用于界面刷新计数等。</summary>
    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<FavoriteItem>>(File.ReadAllText(_filePath));
            if (list == null) return;
            Items.Clear();
            foreach (var item in list) Items.Add(item);
        }
        catch { /* 文件损坏则当作空收藏夹 */ }
    }

    public bool Contains(string text) => Items.Any(i => i.Text == text);

    /// <summary>切换收藏状态，返回切换后是否为已收藏。</summary>
    public bool Toggle(Quote quote)
    {
        var existing = Items.FirstOrDefault(i => i.Text == quote.Text);
        if (existing != null)
        {
            Items.Remove(existing);
            Save();
            Changed?.Invoke();
            return false;
        }

        Items.Insert(0, new FavoriteItem
        {
            Text = quote.Text,
            Source = quote.Source,
            Category = quote.Category,
            SavedAt = DateTime.Now
        });
        Save();
        Changed?.Invoke();
        return true;
    }

    public void Remove(FavoriteItem item)
    {
        Items.Remove(item);
        Save();
        Changed?.Invoke();
    }

    public int Export(string path, bool markdown)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (markdown)
        {
            File.WriteAllLines(path, Items.Select(i =>
                $"- 「{i.Text}」"
                + (string.IsNullOrEmpty(i.Source) ? "" : $" —— {i.Source}")
                + $"（{i.CategoryName} · 收藏于 {i.SavedAtText}）"
                + (i.Tags.Count == 0 ? "" : $" 标签：{i.TagsText}")));
        }
        else
        {
            File.WriteAllLines(path, Items.Select(i =>
                i.Text
                + (string.IsNullOrEmpty(i.Source) ? "" : $"\n—— {i.Source}")
                + $"\n（{i.CategoryName} · 收藏于 {i.SavedAtText}）"
                + (i.Tags.Count == 0 ? "" : $"\n标签：{i.TagsText}") + "\n"));
        }
        return Items.Count;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Items.ToList(), Options));
        }
        catch { }
    }

    /// <summary>供收藏夹窗口编辑标签后手动落盘。</summary>
    public void SaveNow() => Save();
}
