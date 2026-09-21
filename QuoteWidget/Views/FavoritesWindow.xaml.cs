using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QuoteWidget.Models;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class FavoritesWindow : Window
{
    public FavoritesWindow()
    {
        InitializeComponent();
        AppServices.Favorites.Changed += RefreshList;
        List.SelectionChanged += (_, _) =>
        {
            TagEditBox.Text = List.SelectedItem is FavoriteItem item ? item.TagsText : "";
        };
        RefreshList();
    }

    /// <summary>当前筛选标签；null = 全部。</summary>
    private string? _filterTag;

    private string SearchText => SearchBox?.Text?.Trim() ?? "";

    private IEnumerable<FavoriteItem> VisibleItems
    {
        get
        {
            var query = SearchText;
            return AppServices.Favorites.Items
                .Where(i => _filterTag == null || i.Tags.Contains(_filterTag))
                .Where(i => query.Length == 0
                         || i.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || i.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || i.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void RefreshList()
    {
        var items = VisibleItems.ToList();
        int total = AppServices.Favorites.Items.Count;
        CountText.Text = _filterTag == null
            ? $"共 {total} 条收藏"
            : $"标签「{_filterTag}」：{items.Count} 条 / 共 {total} 条";
        List.ItemsSource = items;
        bool empty = total == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = items.Count == 0 && !empty ? Visibility.Collapsed : Visibility.Visible;

        // 刷新标签筛选下拉（保持当前选择）
        var tags = AppServices.Favorites.Items
            .SelectMany(i => i.Tags).Distinct().OrderBy(t => t, StringComparer.CurrentCulture).ToList();
        var previous = _filterTag;
        TagFilterCombo.ItemsSource = new[] { "全部" }.Concat(tags).ToList();
        TagFilterCombo.SelectedItem = previous != null && tags.Contains(previous) ? previous : "全部";
        if (previous != null && !tags.Contains(previous)) _filterTag = null;
    }

    private void OnTagFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagFilterCombo.SelectedItem is string tag && tag != "全部") _filterTag = tag;
        else _filterTag = null;
        RefreshList();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void OnSaveTags(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FavoriteItem item)
        {
            MessageBox.Show("请先选中一条收藏。", "拾句", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        item.Tags = TagEditBox.Text
            .Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        AppServices.Favorites.SaveNow();
        RefreshList();
        List.SelectedItem = item;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FavoriteItem item)
            AppServices.Favorites.Remove(item);
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is not FavoriteItem item) return;
        App.Current.ShowQuote(new Quote
        {
            Text = item.Text,
            Source = item.Source,
            Category = item.Category
        });
    }

    private void OnExportTxt(object sender, RoutedEventArgs e) => Export(markdown: false);

    private void OnExportMd(object sender, RoutedEventArgs e) => Export(markdown: true);

    private void Export(bool markdown)
    {
        if (AppServices.Favorites.Items.Count == 0)
        {
            MessageBox.Show("收藏夹是空的。", "拾句", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            FileName = markdown ? "拾句收藏.md" : "拾句收藏.txt",
            Filter = markdown ? "Markdown 文件|*.md|文本文件|*.txt" : "文本文件|*.txt|Markdown 文件|*.md"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            int count = AppServices.Favorites.Export(dialog.FileName, markdown);
            MessageBox.Show($"已导出 {count} 条收藏到：\n{dialog.FileName}", "拾句",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "拾句", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        AppServices.Favorites.Changed -= RefreshList;
        base.OnClosed(e);
    }
}
