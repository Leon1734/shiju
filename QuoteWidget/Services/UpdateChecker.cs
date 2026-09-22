using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace QuoteWidget.Services;

/// <summary>
/// 更新检查：读取一个 JSON 清单（{"version":"2.3.0","url":"https://...","notes":"..."}），
/// 与当前程序版本比较。地址留空即停用；只提示不自动替换。
/// </summary>
public static class UpdateChecker
{
    public sealed record UpdateInfo(string LatestVersion, string DownloadUrl, string Notes, string FileUrl = "");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>解析清单 JSON；缺 version 视为无效。</summary>
    public static UpdateInfo? ParseManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(version)) return null;
            var download = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
            var file = root.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
            return new UpdateInfo(version, download, notes, file);
        }
        catch { return null; }
    }

    /// <summary>检查更新；无新版本、未配置或失败时返回 null。</summary>
    public static async Task<UpdateInfo?> CheckAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var body = await Http.GetStringAsync(url);
            var info = ParseManifest(body);
            if (info == null) return null;
            return IsNewer(info.LatestVersion, CurrentVersion()) ? info : null;
        }
        catch (Exception ex)
        {
            Log.Warn("update: 检查失败 " + ex.Message);
            return null;
        }
    }

    public static Version CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>语义比较 latest 是否比 current 新（支持 2~4 段数字，缺段按 0）。</summary>
    public static bool IsNewer(string latest, Version current)
    {
        if (!TryParseVersion(latest, out var v)) return false;
        // Version 的未指定段（Build/Revision）可能是 -1，统一按 0 归一化后逐段比较
        int[] a = { v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0) };
        int[] b = { current.Major, current.Minor, Math.Max(current.Build, 0), Math.Max(current.Revision, 0) };
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return a[i] > b[i];
        }
        return false;
    }

    public static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = text.Trim().TrimStart('v', 'V');
        var parts = clean.Split('.');
        if (parts.Length is < 2 or > 4) return false;
        var nums = new List<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n) || n < 0) return false;
            nums.Add(n);
        }
        while (nums.Count < 4) nums.Add(0);
        version = new Version(nums[0], nums[1], nums[2], nums[3]);
        return true;
    }

    // ———————— GitHub 源（无需自建清单服务器） ————————

    public sealed record CommitInfo(string Sha, string ShortSha, string Message, string Date, string HtmlUrl);

    /// <summary>检查 GitHub 最新 Release：有比当前更新的版本时返回更新信息（含 zip 附件直链）。</summary>
    public static async Task<UpdateInfo?> CheckGitHubReleaseAsync(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        try
        {
            var body = await GitHubHttp.Instance.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var version = tag.TrimStart('v', 'V');
            if (!IsNewer(version, CurrentVersion())) return null;

            var pageUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var notesRaw = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var notes = notesRaw.Length > 400 ? notesRaw[..400] + "…" : notesRaw;

            var fileUrl = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        fileUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        if (fileUrl.Length > 0) break;
                    }
                }
            }

            var zipState = fileUrl.Length > 0 ? "有" : "无";
            Log.Info($"update: GitHub Release v{version}（当前 v{CurrentVersion().ToString(3)}，zip={zipState}）");
            return new UpdateInfo(version, pageUrl, notes, fileUrl);
        }
        catch (Exception ex)
        {
            Log.Warn("update: GitHub Release 检查失败 " + ex.Message);
            return null;
        }
    }

    /// <summary>获取分支最新提交（用于"有新提交"提醒）。</summary>
    public static async Task<CommitInfo?> GetLatestCommitAsync(string repo, string branch = "main")
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        try
        {
            var body = await GitHubHttp.Instance.GetStringAsync($"https://api.github.com/repos/{repo}/commits/{branch}");
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var sha = root.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
            if (sha.Length == 0) return null;
            var html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var message = "";
            var date = "";
            if (root.TryGetProperty("commit", out var commit))
            {
                if (commit.TryGetProperty("message", out var m) && m.GetString() is { } full)
                    message = full.Split('\n')[0];
                if (commit.TryGetProperty("author", out var author) && author.TryGetProperty("date", out var d))
                    date = d.GetString() ?? "";
            }
            return new CommitInfo(sha, sha.Length >= 7 ? sha[..7] : sha, message, date, html);
        }
        catch (Exception ex)
        {
            Log.Warn("update: GitHub 提交检查失败 " + ex.Message);
            return null;
        }
    }
}
