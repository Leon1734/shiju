using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuoteWidget.Models;
using QuoteWidget.Services;

namespace QuoteWidget.ViewModels;

/// <summary>
/// 挂件的句子状态与浏览历史。
/// 「取新句」与「显示」分离：窗口先淡出，再 SetCurrent，然后淡入。
/// </summary>
public class WidgetViewModel : INotifyPropertyChanged
{
    private readonly List<Quote> _history = new();
    private int _position = -1;
    private Quote? _current;

    private string _text = "正在为你准备第一句话…";
    private string _sourceDisplay = "";
    private bool _isFavorite;
    private string _translation = "";
    private string _holidayBadge = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get => _text; private set => Set(ref _text, value); }
    public string SourceDisplay { get => _sourceDisplay; private set => Set(ref _sourceDisplay, value); }
    public bool IsFavorite { get => _isFavorite; private set => Set(ref _isFavorite, value); }

    /// <summary>当前句子的译文（右键"翻译当前句"后显示，换句即清空）。</summary>
    public string Translation { get => _translation; private set => Set(ref _translation, value); }
    public bool HasTranslation => Translation.Length > 0;

    /// <summary>节日角标文字（如 "🏮 春节"），非节日为空。</summary>
    public string HolidayBadge { get => _holidayBadge; private set => Set(ref _holidayBadge, value); }

    public string FavoriteLabel => IsFavorite ? "♥ 已收藏" : "♡ 收藏";
    public bool CanBack => _position > 0;
    public bool CanForward => _position < _history.Count - 1;

    public Quote? CurrentQuote => _current;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void RaiseNavigation()
    {
        Raise(nameof(CanBack));
        Raise(nameof(CanForward));
    }

    /// <summary>把句子设为当前显示（含收藏状态刷新），并记住它是"本次运行最后一句"。</summary>
    public void SetCurrent(Quote quote)
    {
        _current = quote;
        Text = quote.Text;
        SourceDisplay = string.IsNullOrEmpty(quote.Source) ? "" : "—— " + quote.Source;
        AppServices.Settings.LastQuoteText = quote.Text;
        RefreshFavorite();
    }

    public void RefreshFavorite()
    {
        IsFavorite = _current != null && AppServices.Favorites.Contains(_current.Text);
        Raise(nameof(FavoriteLabel));
    }

    /// <summary>打字机逐字显示时由窗口逐步写入预览文本。</summary>
    public void SetPreviewText(string text) => Text = text;

    /// <summary>显示译文；传 null 清除。</summary>
    public void SetTranslation(string? text)
    {
        Translation = text ?? "";
        Raise(nameof(HasTranslation));
    }

    /// <summary>刷新节日角标（设置或日期变化时调用）。</summary>
    public void RefreshHolidayBadge()
    {
        HolidayBadge = AppServices.Settings.HolidayEgg
            ? HolidayService.GetToday() ?? ""
            : "";
    }

    /// <summary>
    /// 取一句新话（开启一言则先尝试在线，失败回退内置词库），
    /// 写入浏览历史并返回；显示仍由窗口控制动画时序。
    /// 在线获取最多等 500ms；会话内最近 50 句去重，开局避开上次最后一句。
    /// 节日彩蛋开启且当天是节日时，优先出节日祝福包的句子。
    /// </summary>
    public async Task<Quote?> FetchNewAsync()
    {
        Quote? online = null;
        if (AppServices.Settings.UseHitokoto)
        {
            var fetchTask = AppServices.Hitokoto.FetchAsync();
            var finished = await Task.WhenAny(fetchTask, Task.Delay(500));
            if (finished == fetchTask) online = fetchTask.Result;
        }

        Quote? quote = online;
        if (quote != null && RecentlySeen(quote.Text))
            quote = null; // 一言服务端可能连续吐重复句：弃用，回退本地

        if (quote == null)
        {
            quote = AppServices.Quotes.Next(AppServices.Settings.RandomMode);
            if (RecentlySeen(quote.Text))
                quote = AppServices.Quotes.Next(AppServices.Settings.RandomMode);
        }

        if (quote.Text == _current?.Text)
            quote = AppServices.Quotes.Next(AppServices.Settings.RandomMode);

        // 节日彩蛋：当天开场第一句优先出节日祝福包的应景句
        if (_current == null && AppServices.Settings.HolidayEgg && HolidayService.GetToday() != null)
        {
            var holidayQuote = AppServices.Quotes.HolidayQuotes()
                .FirstOrDefault(q => q.Text != _current?.Text && !RecentlySeen(q.Text));
            if (holidayQuote != null) quote = holidayQuote;
        }

        // 新会话第一句别和上次关闭前的撞车
        if (_current == null && quote.Text == AppServices.Settings.LastQuoteText)
        {
            var another = AppServices.Quotes.Next(AppServices.Settings.RandomMode);
            if (another.Text != quote.Text) quote = another;
        }

        Remember(quote.Text);
        _history.Add(quote);
        if (_history.Count > 200)
        {
            _history.RemoveAt(0);
            _position--;
        }
        _position = _history.Count - 1;
        RaiseNavigation();
        return quote;
    }

    private readonly List<string> _recentTexts = new();

    private bool RecentlySeen(string text) => _recentTexts.Contains(text);

    private void Remember(string text)
    {
        _recentTexts.Remove(text);
        _recentTexts.Add(text);
        if (_recentTexts.Count > 50) _recentTexts.RemoveAt(0);
    }

    /// <summary>
    /// 取今日一句（确定性：同一天重启多次都是同一句）。
    /// 若当前显示的已经是今日一句则返回 null，由窗口保持原样。
    /// </summary>
    public Quote? FetchDaily()
    {
        var quote = AppServices.Quotes.DailyQuote(DateTime.Now.Date, AppServices.Settings.HolidayEgg);
        if (quote == null || quote.Text == _current?.Text) return null;
        Remember(quote.Text);
        _history.Add(quote);
        _position = _history.Count - 1;
        RaiseNavigation();
        return quote;
    }

    /// <summary>后退到上一条历史并返回它（显示由窗口控制）。</summary>
    public Quote? MoveBack()
    {
        if (!CanBack) return null;
        _position--;
        RaiseNavigation();
        return _history[_position];
    }

    /// <summary>前进到下一条历史并返回它（显示由窗口控制）。</summary>
    public Quote? MoveForward()
    {
        if (!CanForward) return null;
        _position++;
        RaiseNavigation();
        return _history[_position];
    }
}
