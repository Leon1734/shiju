using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>
/// 软件升级：从清单直链下载升级包（zip/exe）或选择本地包，
/// 写入 %APPDATA%\QuoteWidget\update\ 后用批处理完成"等待退出→替换→重启"。
/// </summary>
public static class UpdateService
{
    public static string UpdateDir => Path.Combine(SettingsStore.DataDir, "update");

    /// <summary>下载升级包并解出新的 QuoteWidget.exe，返回新 exe 路径。</summary>
    public static async Task<string> DownloadAsync(UpdateChecker.UpdateInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.FileUrl))
            throw new InvalidOperationException("更新清单未提供升级包直链（file 字段）");
        Directory.CreateDirectory(UpdateDir);
        var raw = Path.Combine(UpdateDir, "package.raw");
        var target = Path.Combine(UpdateDir, "QuoteWidget.new.exe");

        // 用带 DoH 直连能力的客户端：GitHub 域名被 hosts 拦截/直连抖动时仍可下载
        var bytes = await GitHubHttp.Instance.GetByteArrayAsync(info.FileUrl);
        if (bytes.Length == 0) throw new InvalidOperationException("下载内容为空");
        await File.WriteAllBytesAsync(raw, bytes);
        ExtractExe(raw, target);
        return target;
    }

    /// <summary>从本地包（zip 或 exe）准备新 exe。</summary>
    public static void ApplyLocalPackage(string packagePath)
    {
        Directory.CreateDirectory(UpdateDir);
        var target = Path.Combine(UpdateDir, "QuoteWidget.new.exe");
        ExtractExe(packagePath, target);
        ApplyAndRestart(target);
    }

    /// <summary>写出替换脚本并重启程序（脚本会等本进程退出后覆盖再拉起）。</summary>
    public static void ApplyAndRestart(string newExePath)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) throw new InvalidOperationException("无法确定当前程序路径");

        var bat = Path.Combine(UpdateDir, "update.bat");
        // 等待旧进程完全退出后替换。注意：隐藏控制台下 timeout 会立即失败，
        // 改用 ping 做延时，并用重试循环应对文件锁尚未释放的情况。
        var script = $"""
            @echo off
            ping -n 3 127.0.0.1 >nul
            for /l %%i in (1,1,30) do (
              copy /y "{newExePath}" "{current}" >nul 2>&1 && goto run
              ping -n 2 127.0.0.1 >nul
            )
            :run
            start "" "{current}"
            del "%~f0"
            """;
        File.WriteAllText(bat, script);
        Log.Info("update: 应用升级并重启 " + newExePath);
        Process.Start(new ProcessStartInfo(bat) { CreateNoWindow = true, UseShellExecute = false });
        Application.Current.Shutdown();
    }

    /// <summary>按文件内容（PK/MZ 魔数）判断是 zip 还是 exe，不依赖扩展名。</summary>
    private static void ExtractExe(string packagePath, string targetExe)
    {
        var head = new byte[4];
        using (var fs = File.OpenRead(packagePath)) fs.ReadExactly(head, 0, 4);

        bool isZip = head[0] == 0x50 && head[1] == 0x4B;      // 'PK' → zip
        bool isExe = head[0] == 0x4D && head[1] == 0x5A;      // 'MZ' → Windows 程序

        if (isZip)
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("QuoteWidget.exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("压缩包里没有找到 QuoteWidget.exe");
            entry.ExtractToFile(targetExe, true);
        }
        else if (isExe)
        {
            File.Copy(packagePath, targetExe, true);
        }
        else
        {
            throw new InvalidOperationException("升级包不是有效的 Windows 程序或压缩包（可能下载到了错误内容）");
        }
    }
}
