using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>
/// 词库：内置四类台词在程序目录 词库\ 下以 md 文件形式存放（可直接编辑增补、随包发布），
/// 首次运行自动从内嵌 quotes.json 生成；其余 .md/.txt 文件各成一个自定义分类。
/// 按设置过滤分类；随机用洗牌袋（一轮内不重复），也支持顺序播放。
/// </summary>
public class QuoteRepository
{
    /// <summary>词库文件夹（在程序运行目录下，方便随包发布与手动编辑）。</summary>
    public static readonly string BankDir = Path.Combine(AppContext.BaseDirectory, "词库");

    private static readonly (string File, string Category, string Title)[] BuiltInBanks =
    {
        ("电影台词.md", "movie", "电影台词"),
        ("游戏台词.md", "game", "游戏台词"),
        ("小说名句.md", "novel", "小说名句"),
        ("音乐歌词.md", "lyric", "音乐歌词")
    };

    private List<Quote> _all = new();          // 内嵌兜底（仅当某分类写盘失败时使用）
    private readonly HashSet<string> _diskSourced = new(); // 已由磁盘文件负责的分类，避免重复
    private List<Quote> _pool = new();
    private readonly List<Quote> _bag = new();
    private int _bagIndex;
    private int _seqIndex = -1;
    private Quote? _lastEmitted;

    public int TotalCount => _all.Count + DiskQuoteCount();
    public int PoolCount => _pool.Count;

    /// <summary>是否为内置四类的词库文件（这些分类由固定开关控制，不出现在自定义列表里）。</summary>
    public static bool IsBuiltInBankFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return BuiltInBanks.Any(b => b.File.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    public void LoadFromResource()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/quotes.json"));
        if (info != null)
        {
            using var stream = info.Stream;
            // quotes.json 由脚本生成，字段为小写：必须开启大小写不敏感匹配
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var file = JsonSerializer.Deserialize<QuotesFile>(stream, options);
            _all = file?.Quotes?.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList() ?? new List<Quote>();
        }
        ExportBuiltInBankFiles();
    }

    /// <summary>把内置四类台词导出为词库文件夹里的 md 文件（已有内容的文件不覆盖，保留用户编辑）。</summary>
    private void ExportBuiltInBankFiles()
    {
        _diskSourced.Clear();
        try
        {
            Directory.CreateDirectory(BankDir);
            foreach (var (fileName, category, title) in BuiltInBanks)
            {
                var path = Path.Combine(BankDir, fileName);
                bool needsGenerate = true;
                if (File.Exists(path))
                {
                    // 已存在的文件：有实际内容就保留（尊重用户编辑），空壳则重建
                    if (ParseBankFile(path, category).Count > 0)
                    {
                        needsGenerate = false;
                    }
                }
                if (needsGenerate)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"# {title}（可直接增删改：每行一句，—— 后为出处；删除本文件后重启程序会重新生成）");
                    int n = 0;
                    foreach (var q in _all.Where(q => q.Category == category))
                        sb.Append(++n).Append(". ").Append(q.Text)
                          .AppendLine(string.IsNullOrEmpty(q.Source) ? "" : $"——{q.Source}");
                    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
                _diskSourced.Add(category); // 磁盘文件为准，内嵌数据不再参与，避免重复
            }
            _all = _all.Where(q => !_diskSourced.Contains(q.Category)).ToList();
        }
        catch
        {
            // 程序目录只读等场景：保留内嵌词库兜底，跳过磁盘扫描对应分类
        }
    }

    public void Reload(AppSettings settings)
    {
        bool Enabled(Quote q) => q.Category switch
        {
            "movie" => settings.CatMovie,
            "game" => settings.CatGame,
            "novel" => settings.CatNovel,
            "lyric" => settings.CatLyric,
            _ => true
        };

        _pool = _all.Where(Enabled).ToList();
        _pool.AddRange(LoadCustomQuotes(settings));
        if (_pool.Count == 0) _pool = new List<Quote>(_all); // 全部关闭时不让挂件没词可说
        _bag.Clear();
        _bagIndex = 0;
        _seqIndex = -1;
    }

    /// <summary>扫描词库文件夹：四个内置文件映射到固定分类，其余文件各成一个自定义分类。</summary>
    private List<Quote> LoadCustomQuotes(AppSettings settings)
    {
        var result = new List<Quote>();
        if (!Directory.Exists(BankDir)) return result;

        var files = Directory.EnumerateFiles(BankDir)
            .Where(f => (f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                      || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                     && !Path.GetFileName(f).StartsWith("readme", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.CurrentCulture);

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var builtIn = BuiltInBanks.FirstOrDefault(b => b.File.Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
            if (builtIn.Category != null)
            {
                if (!_diskSourced.Contains(builtIn.Category)) continue; // 该分类走内嵌兜底，别重复
                bool catEnabled = builtIn.Category switch
                {
                    "movie" => settings.CatMovie,
                    "game" => settings.CatGame,
                    "novel" => settings.CatNovel,
                    "lyric" => settings.CatLyric,
                    _ => true
                };
                if (!catEnabled) continue;
                if (!ScheduleActive(settings, name)) continue; // 定时词库：时段外不加载
                result.AddRange(ParseBankFile(file, builtIn.Category));
            }
            else
            {
                bool enabled = !settings.CustomCategories.TryGetValue(name, out var on) || on; // 默认启用
                if (!enabled) continue;
                if (!ScheduleActive(settings, name)) continue; // 定时词库：时段外不加载
                result.AddRange(ParseBankFile(file, "custom:" + name));
            }
        }
        return result;
    }

    /// <summary>查询词库时段设置并判断当前小时是否生效。</summary>
    private static bool ScheduleActive(AppSettings settings, string bankName)
    {
        int schedule = settings.BankSchedules.TryGetValue(bankName, out var s) ? s : 0;
        return IsScheduleActive(schedule, DateTime.Now.Hour);
    }

    /// <summary>
    /// 解析一个词库文件，支持多种写法混用：
    /// ① 单行句：句子——出处   ② 编号行：3. 句子——出处   ③ 多行句：缩进的行并入上一句
    /// （适合"英文 + 中文翻译"两行一句），直到遇到空行或新的编号行才结束。
    /// 忽略空行 / #标题 / --- 分隔线 / README 文件。
    /// </summary>
    public static List<Quote> ParseBankFile(string path, string category)
    {
        var list = new List<Quote>();
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return list; }

        var buffer = new List<string>();
        string pendingSource = "";

        void Flush()
        {
            var text = string.Join("\n", buffer).Trim();
            if (text.Length > 0)
                list.Add(new Quote
                {
                    Id = $"{category}-{list.Count + 1}",
                    Text = text,
                    Source = pendingSource,
                    Category = category
                });
            buffer.Clear();
            pendingSource = "";
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush(); // 空行 = 分隔
                continue;
            }
            if (line.StartsWith("#") || line.StartsWith("---")) continue;

            bool indented = raw[0] == ' ' || raw[0] == '\t'; // 缩进行 = 上一句的续行
            var body = line;
            var m = Regex.Match(line, @"^(\d+)[\.、．]\s*(.+)$");
            if (m.Success)
            {
                body = m.Groups[2].Value.Trim();
                Flush(); // 编号行一定开新句
            }
            if (body.Length == 0) continue;

            var sep = body.LastIndexOf("——", StringComparison.Ordinal);
            if (sep >= 0)
            {
                var textPart = body[..sep].Trim();
                if (textPart.Length > 0) buffer.Add(textPart);
                pendingSource = body[(sep + 2)..].Trim();
                Flush(); // —— 行一定是本句收尾
            }
            else if (indented)
            {
                buffer.Add(body); // 无条件并入上一句
            }
            else
            {
                // 上一行已以句末标点收尾时，视作新的一句（兼容"每行一句"的纯文本词库）
                if (buffer.Count > 0 && EndsWithSentence(buffer[^1]))
                    Flush();
                buffer.Add(body);
            }
        }
        Flush();
        return list;
    }

    private static bool EndsWithSentence(string s)
    {
        char c = s[^1];
        return c is '.' or '!' or '?' or '。' or '！' or '？' or '”' or '"' or '…';
    }

    /// <summary>词库体检问题：文件、行号（0=文件级）、问题描述。</summary>
    public sealed record BankDiagnostic(string File, int Line, string Issue);

    /// <summary>
    /// 词库健康检查：找缩进续行缺主句、编号句缺译文（仅对含多行句写法的文件）、
    /// 编号后没内容、行内容重复、超长句。只诊断，不改文件。
    /// </summary>
    public static List<BankDiagnostic> AnalyzeBankFile(string path)
    {
        var issues = new List<BankDiagnostic>();
        var fileName = Path.GetFileName(path);
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex)
        {
            issues.Add(new BankDiagnostic(fileName, 0, "无法读取：" + ex.Message));
            return issues;
        }

        bool usesIndentedStyle = lines.Any(l => l.Length > 0 && (l[0] == ' ' || l[0] == '\t'));
        // 缩进译文格式需在文件中成立至少 3 次且覆盖约四分之一以上编号句，
        // 才启用"缺译文"检查，避免混排文件（大量单行句+个别多行句）误报
        int indentedPairs = 0;
        int numberedCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var cur = lines[i];
            var prev = i > 0 ? lines[i - 1].Trim() : "";
            var prevM = Regex.Match(prev, @"^(\d+)[\.、．]\s*");
            var curM = Regex.Match(cur.Trim(), @"^(\d+)[\.、．]\s*");
            if (curM.Success) numberedCount++;
            if (cur.Length > 0 && (cur[0] == ' ' || cur[0] == '\t') && prevM.Success)
            {
                indentedPairs++;
            }
        }
        bool enforceTranslation = indentedPairs >= 1 && indentedPairs * 4 >= numberedCount;
        var seenLines = new Dictionary<string, int>(StringComparer.Ordinal);
        bool prevNumberedPending = false; // 上一个编号句还在等译文行
        int prevNumberedLine = 0;
        bool contentSinceFlush = false;   // 空行/编号后是否已有主句内容

        void SettlePending()
        {
            if (prevNumberedPending && enforceTranslation)
                issues.Add(new BankDiagnostic(fileName, prevNumberedLine, "编号句似乎缺少译文行"));
            prevNumberedPending = false;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0)
            {
                SettlePending();
                contentSinceFlush = false;
                continue;
            }
            if (line.StartsWith("#") || line.StartsWith("---")) continue;

            bool indented = raw[0] == ' ' || raw[0] == '\t';
            var m = Regex.Match(line, @"^(\d+)[\.、．]\s*(.*)$");
            string body;
            if (m.Success)
            {
                SettlePending();
                prevNumberedLine = lineNo;
                body = m.Groups[2].Value.Trim();
                if (body.Length == 0)
                {
                    issues.Add(new BankDiagnostic(fileName, lineNo, "编号后没有内容"));
                    contentSinceFlush = false;
                    continue;
                }
                prevNumberedPending = true;
                contentSinceFlush = true;
                if (body.Contains("——")) prevNumberedPending = false; // 句子自带出处，无需译文行
            }
            else if (indented)
            {
                if (!contentSinceFlush)
                    issues.Add(new BankDiagnostic(fileName, lineNo, "缩进的译文行缺少主句"));
                body = line;
                prevNumberedPending = false; // 缩进行就是译文，静默解除等待
            }
            else
            {
                contentSinceFlush = true;
                body = line;
                if (line.Contains("——")) SettlePending();
            }

            // 行内容重复检查（忽略出处差异）
            var dupKey = body;
            var sep = dupKey.LastIndexOf("——", StringComparison.Ordinal);
            if (sep >= 0) dupKey = dupKey[..sep].Trim();
            if (dupKey.Length > 0 && seenLines.TryGetValue(dupKey, out var firstLine))
                issues.Add(new BankDiagnostic(fileName, lineNo, $"与第 {firstLine} 行内容重复"));
            else if (dupKey.Length > 0)
                seenLines[dupKey] = lineNo;

            if (body.Length > 200)
                issues.Add(new BankDiagnostic(fileName, lineNo, $"句子过长（{body.Length} 字），显示时可能放不下"));
        }

        SettlePending();
        return issues;
    }

    private int DiskQuoteCount()
    {
        if (!Directory.Exists(BankDir)) return 0;
        return Directory.EnumerateFiles(BankDir)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Sum(f => ParseBankFile(f, "count").Count);
    }

    /// <summary>
    /// 每日一句：按日期做种子从当前词库池确定性地取一句——
    /// 同一天无论重启多少次都是同一句，跨零点自然更换。
    /// 节日彩蛋开启且当天是节日、词库里有节日祝福包时，改为从节日包里确定性取一句。
    /// </summary>
    public Quote? DailyQuote(DateTime date, bool preferHoliday)
    {
        var pool = preferHoliday && HolidayService.GetToday(date) != null
            ? HolidayQuotes()
            : _pool;
        if (pool.Count == 0) return null;
        int seed = date.Year * 10000 + date.Month * 100 + date.Day;
        int index = new Random(seed).Next(pool.Count);
        return pool[index];
    }

    /// <summary>当前词库池里的节日祝福句（自定义词库"节日祝福"），没有则空列表。</summary>
    public List<Quote> HolidayQuotes() =>
        _pool.Where(q => q.Category == "custom:节日祝福").ToList();

    /// <summary>词库时段是否在指定小时生效。0=全天，1=白天 8-22 点，2=夜间 22-8 点。</summary>
    public static bool IsScheduleActive(int schedule, int hour) => schedule switch
    {
        1 => hour >= 8 && hour < 22,
        2 => hour >= 22 || hour < 8,
        _ => true
    };

    public Quote Next(bool random)
    {
        Quote quote;
        if (_pool.Count == 0)
        {
            quote = new Quote { Id = "empty", Text = "词库为空，请在设置中开启分类", Source = "", Category = "" };
        }
        else if (random)
        {
            if (_bagIndex >= _bag.Count) RefillBag();
            quote = _bag[_bagIndex++];
        }
        else
        {
            _seqIndex = (_seqIndex + 1) % _pool.Count;
            quote = _pool[_seqIndex];
        }
        _lastEmitted = quote;
        return quote;
    }

    private void RefillBag()
    {
        _bag.Clear();
        _bag.AddRange(_pool);
        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }
        // 避免新一轮第一句与上一轮最后一句重复
        if (_bag.Count > 1 && _lastEmitted != null && _bag[0].Id == _lastEmitted.Id)
            (_bag[0], _bag[^1]) = (_bag[^1], _bag[0]);
        _bagIndex = 0;
    }
}
