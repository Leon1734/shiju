using System.IO;
using System.Net.Http;
using System.Reflection;

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
            using var doc = System.Text.Json.JsonDocument.Parse(json);
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
}
