using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace QuoteWidget.Services;

/// <summary>
/// 访问 GitHub 的 HttpClient：本机 hosts 把 api.github.com 等域名指向 127.0.0.1，
/// 且国内直连 IP 时常抖动。这里通过阿里 DoH（223.5.5.5，纯 IP 访问不受 hosts 影响）
/// 解析真实 IP 后直连，等效于 curl 的 --resolve；DoH 失败时退回系统 DNS。
/// </summary>
public static class GitHubHttp
{
    private static readonly HttpClient Client = Create();

    public static HttpClient Instance => Client;

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                IPAddress[] addresses;
                if (IPAddress.TryParse(host, out var literal))
                {
                    addresses = new[] { literal };
                }
                else
                {
                    var resolved = await Doh.ResolveAsync(host, cancellationToken);
                    addresses = resolved != null
                        ? new[] { IPAddress.Parse(resolved) }
                        : await Dns.GetHostAddressesAsync(host, cancellationToken);
                }

                Exception? last = null;
                foreach (var address in addresses)
                {
                    try
                    {
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                    }
                }
                throw last ?? new SocketException((int)SocketError.HostUnreachable);
            }
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShiJu-QuoteWidget/2.5");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>阿里公共 DNS 的 DoH 解析（走 IP，绕过 hosts 拦截），带 5 分钟缓存。</summary>
    private static class Doh
    {
        private static readonly HttpClient Plain = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly Dictionary<string, (string Ip, DateTime At)> Cache = new();
        private static readonly object Lock = new();

        public static async Task<string?> ResolveAsync(string host, CancellationToken ct)
        {
            lock (Lock)
            {
                if (Cache.TryGetValue(host, out var hit) && DateTime.Now - hit.At < TimeSpan.FromMinutes(5))
                    return hit.Ip;
            }
            try
            {
                var json = await Plain.GetStringAsync(
                    $"https://223.5.5.5/resolve?name={Uri.EscapeDataString(host)}&type=A", ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Answer", out var answers))
                {
                    foreach (var answer in answers.EnumerateArray())
                    {
                        if (answer.TryGetProperty("data", out var data) && data.GetString() is { } ip
                            && IPAddress.TryParse(ip, out _))
                        {
                            lock (Lock) Cache[host] = (ip, DateTime.Now);
                            Log.Info($"github-http: DoH 解析 {host} -> {ip}");
                            return ip;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"github-http: DoH 解析 {host} 失败（回退系统 DNS）：{ex.Message}");
            }
            return null;
        }
    }
}
