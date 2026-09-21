using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>在线「一言」(hitokoto.cn)：取句失败时返回 null，由调用方回退内置词库。</summary>
public class HitokotoService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private sealed class HitokotoDto
    {
        [JsonPropertyName("hitokoto")] public string Text { get; set; } = "";
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("from_who")] public string? FromWho { get; set; }
    }

    public async Task<Quote?> FetchAsync()
    {
        try
        {
            var dto = await Http.GetFromJsonAsync<HitokotoDto>(
                "https://v1.hitokoto.cn/?encode=json&c=a&c=b&c=c&c=d&c=h&c=i&c=k");
            if (dto == null || string.IsNullOrWhiteSpace(dto.Text)) return null;

            var source = string.IsNullOrWhiteSpace(dto.FromWho)
                ? $"「{dto.From}」"
                : $"{dto.FromWho}「{dto.From}」";

            return new Quote
            {
                Id = "hitokoto-" + DateTime.Now.Ticks,
                Text = dto.Text.Trim(),
                Source = source.Trim(),
                Category = "hitokoto"
            };
        }
        catch
        {
            return null; // 断网 / 超时：静默回退内置词库
        }
    }
}
