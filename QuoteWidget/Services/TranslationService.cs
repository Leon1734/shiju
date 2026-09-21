using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>翻译结果。</summary>
public sealed record TranslateResult(bool Success, string Output, string? Error)
{
    public static TranslateResult Fail(string error) => new(false, "", error);
    public static TranslateResult Ok(string output) => new(true, output, null);
}

/// <summary>词典词条（TSV：word\t音标\t词性\t释义）。</summary>
public sealed record DictEntry(string Word, string Phonetic, string Pos, string Translation);

/// <summary>
/// OpenAI 兼容的在线大模型翻译引擎（智谱 / 硅基流动 / DeepSeek / 自定义端点共用）。
/// 中文译英文，其他语言译中文；只输出译文。
/// </summary>
public static class LlmEngine
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>引擎预设：Kind / 显示名 / 默认地址 / 默认模型 / 是否需要 Key。</summary>
    public sealed record EnginePreset(string Kind, string Name, string DefaultUrl, string DefaultModel, bool RequiresKey);

    public static readonly EnginePreset[] Presets =
    {
        new("Zhipu", "智谱 GLM-4-Flash（免费）",
            "https://open.bigmodel.cn/api/paas/v4/chat/completions", "glm-4-flash", true),
        new("SiliconFlow", "硅基流动（有免费模型）",
            "https://api.siliconflow.cn/v1/chat/completions", "Qwen/Qwen2.5-7B-Instruct", true),
        new("DeepSeek", "DeepSeek（付费，约 2 元/百万输入）",
            "https://api.deepseek.com/chat/completions", "deepseek-chat", true),
        new("Qwen", "通义千问（有免费额度）",
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "qwen-turbo", true),
        new("Moonshot", "Kimi 月之暗面（付费）",
            "https://api.moonshot.cn/v1/chat/completions", "moonshot-v1-8k", true),
        new("Spark", "讯飞星火（有免费额度）",
            "https://spark-api-open.xf-yun.com/v1/chat/completions", "generalv3.5", true),
        new("Ollama", "本地 Ollama（免费离线，需自行安装）",
            "http://localhost:11434/v1/chat/completions", "qwen2.5:3b", false),
        new("Custom", "自定义 OpenAI 兼容端点", "", "", true),
    };

    public static EnginePreset? GetPreset(string kind) =>
        Presets.FirstOrDefault(p => p.Kind == kind);

    /// <summary>各引擎的 API Key 字段映射。</summary>
    public static string GetApiKey(string kind, AppSettings s) => kind switch
    {
        "Zhipu" => s.ZhipuApiKey,
        "SiliconFlow" => s.SiliconApiKey,
        "DeepSeek" => s.DeepseekApiKey,
        "Qwen" => s.QwenApiKey,
        "Moonshot" => s.MoonshotApiKey,
        "Spark" => s.SparkApiKey,
        "Custom" => s.CustomApiKey,
        _ => ""
    };

    public static void SetApiKey(string kind, AppSettings s, string value)
    {
        switch (kind)
        {
            case "Zhipu": s.ZhipuApiKey = value; break;
            case "SiliconFlow": s.SiliconApiKey = value; break;
            case "DeepSeek": s.DeepseekApiKey = value; break;
            case "Qwen": s.QwenApiKey = value; break;
            case "Moonshot": s.MoonshotApiKey = value; break;
            case "Spark": s.SparkApiKey = value; break;
            case "Custom": s.CustomApiKey = value; break;
        }
    }

    /// <summary>解析引擎实际使用的地址/模型：设置里的自定义值优先，否则用预设默认值。</summary>
    public static (string Url, string Model) Resolve(string kind, AppSettings s)
    {
        var preset = GetPreset(kind);
        var url = s.EngineUrls.TryGetValue(kind, out var u) && !string.IsNullOrWhiteSpace(u)
            ? u : preset?.DefaultUrl ?? "";
        var model = s.EngineModels.TryGetValue(kind, out var m) && !string.IsNullOrWhiteSpace(m)
            ? m : preset?.DefaultModel ?? "";
        return (url, model);
    }

    private const string SystemPrompt =
        "你是精准的翻译引擎。自动检测输入语言：中文翻译成英文，其他语言翻译成简体中文。" +
        "只输出译文本身，不要任何解释、拼音或引号。";

    public static async Task<TranslateResult> TranslateAsync(string text, AppSettings s)
    {
        var preset = GetPreset(s.TranslateEngine);
        if (preset == null) return TranslateResult.Fail("未知翻译引擎：" + s.TranslateEngine);
        var (url, model) = Resolve(preset.Kind, s);
        var key = GetApiKey(preset.Kind, s);
        var name = preset.Name;

        if (string.IsNullOrWhiteSpace(url)) return TranslateResult.Fail("请先在设置中填写请求地址");
        if (string.IsNullOrWhiteSpace(model)) return TranslateResult.Fail("请先在设置中填写模型名");
        if (preset.RequiresKey && string.IsNullOrWhiteSpace(key))
            return TranslateResult.Fail($"请先在设置中填写{name}的 API Key");

        var payload = JsonSerializer.Serialize(new
        {
            model,
            temperature = 0.2,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = text }
            }
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return TranslateResult.Fail($"{name} HTTP {(int)response.StatusCode}：{TrimBody(body)}");

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(content)
                ? TranslateResult.Fail($"{name} 返回为空")
                : TranslateResult.Ok(content!.Trim());
        }
        catch (Exception ex)
        {
            Log.Error($"{name} 翻译失败", ex);
            return TranslateResult.Fail($"{name} 请求失败：{ex.Message}");
        }
    }

    private static string TrimBody(string body) =>
        body.Length <= 200 ? body : body[..200] + "…";

    public static bool ContainsCjk(string text) =>
        text.Any(c => c is >= (char)0x4E00 and <= (char)0x9FFF);
}

/// <summary>
/// 离线词典引擎：程序目录 词典\ecdict-compact.txt（UTF-8 TSV，词\t音标\t词性\t释义）。
/// 短语/单词 → 精确查词；长句 → 逐词直译（标注仅供参考）；中文 → 提示改用在线引擎。
/// </summary>
public static class OfflineDictEngine
{
    public static string DictDir => Path.Combine(AppContext.BaseDirectory, "词典");
    public static string DictPath => Path.Combine(DictDir, "ecdict-compact.txt");

    private static Dictionary<string, DictEntry>? _cache;
    private static readonly object Lock = new();

    /// <summary>词典状态（词条数；文件不存在为 0）。</summary>
    public static (int Count, bool Exists) Status()
    {
        var entries = Load();
        return (entries.Count, File.Exists(DictPath));
    }

    public static Dictionary<string, DictEntry> Load()
    {
        lock (Lock)
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, DictEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var raw in File.ReadAllLines(DictPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 4 || parts[0].Length == 0) continue;
                    _cache[parts[0].Trim()] = new DictEntry(parts[0].Trim(), parts[1].Trim(),
                        parts[2].Trim(), string.Join('\t', parts[3..]).Trim());
                }
            }
            catch { /* 没有词典文件时静默为空 */ }
            return _cache;
        }
    }

    public static void Reload() { lock (Lock) _cache = null; }

    public static Task<TranslateResult> HandleAsync(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return Task.FromResult(TranslateResult.Fail("请输入要翻译的内容"));

        if (LlmEngine.ContainsCjk(trimmed))
            return Task.FromResult(TranslateResult.Fail("离线词典只支持英文查词；中文翻译请切换到在线引擎"));

        var entries = Load();
        if (entries.Count == 0)
            return Task.FromResult(TranslateResult.Fail(
                "未找到离线词典。请把 ecdict-compact.txt 放入程序目录的 词典\\ 文件夹（详见设置里的说明）"));

        var words = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 3)
        {
            var key = words[^1].Trim('.', ',', '!', '?', ';', ':', '\u2019', '"').ToLowerInvariant();
            if (entries.TryGetValue(key, out var entry))
            {
                var output = $"{entry.Word}  {entry.Phonetic}\n{entry.Pos}\n{entry.Translation.Replace('\t', '；')}";
                return Task.FromResult(TranslateResult.Ok(output));
            }
            return Task.FromResult(TranslateResult.Fail($"词典中没有「{key}」，试试在线引擎，或把词条补充到词典文件里"));
        }

        // 逐词直译（降级模式）
        var sb = new StringBuilder();
        foreach (var raw in words)
        {
            var word = raw.Trim('.', ',', '!', '?', ';', ':', '"', '(', ')').ToLowerInvariant();
            if (word.Length == 0) { sb.Append(raw).Append(' '); continue; }
            if (entries.TryGetValue(word, out var e))
                sb.Append(e.Translation.Split('\t')[0].Split('（')[0].Trim()).Append(' ');
            else
                sb.Append(raw).Append(' ');
        }
        return Task.FromResult(TranslateResult.Ok(sb.ToString().Trim() + "\n（离线逐词直译，仅供参考）"));
    }
}

/// <summary>按设置选择引擎并执行翻译。</summary>
public static class TranslationService
{
    public static async Task<TranslateResult> TranslateAsync(string text)
    {
        var s = AppServices.Settings;
        if (s.TranslateEngine == "OfflineDict")
            return await OfflineDictEngine.HandleAsync(text);
        return await LlmEngine.TranslateAsync(text, s);
    }
}
