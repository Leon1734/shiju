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

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var bytes = await http.GetByteArrayAsync(info.FileUrl);
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
        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /y "{newExePath}" "{current}"
            start "" "{current}"
            del "%~f0"
            """;
        File.WriteAllText(bat, script);
        Log.Info("update: 应用升级并重启 " + newExePath);
        Process.Start(new ProcessStartInfo(bat) { CreateNoWindow = true, UseShellExecute = false });
        Application.Current.Shutdown();
    }

    private static void ExtractExe(string packagePath, string targetExe)
    {
        if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("QuoteWidget.exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("压缩包里没有找到 QuoteWidget.exe");
            entry.ExtractToFile(targetExe, true);
        }
        else
        {
            File.Copy(packagePath, targetExe, true);
        }
    }
}
