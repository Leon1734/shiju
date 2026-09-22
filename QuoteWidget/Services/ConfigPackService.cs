using System.IO;
using System.IO.Compression;
using System.Text;

namespace QuoteWidget.Services;

/// <summary>
/// 配置与内容打包：一键把「设置 + 收藏 + 词库 + 词典」导出为 zip（换电脑/分享），
/// 并可反向导入；也支持只导出词库包分享给朋友。
/// </summary>
public static class ConfigPackService
{
    public sealed record ImportSummary(int Settings, int Favorites, int Banks, int Dictionaries);

    /// <summary>导出完整配置包。</summary>
    public static string ExportSettings(string targetZip)
    {
        SettingsStore.Save(AppServices.Settings); // 确保磁盘上是最新设置
        using var zip = ZipFile.Open(targetZip, ZipArchiveMode.Create);

        AddFile(zip, Path.Combine(SettingsStore.DataDir, "settings.json"), "settings.json");
        AddFile(zip, Path.Combine(SettingsStore.DataDir, "favorites.json"), "favorites.json");

        foreach (var file in EnumerateDir(QuoteRepository.BankDir, "*.md")
                     .Concat(EnumerateDir(QuoteRepository.BankDir, "*.txt")))
        {
            if (Path.GetFileName(file).StartsWith("README", StringComparison.OrdinalIgnoreCase)) continue;
            AddFile(zip, file, "词库/" + Path.GetFileName(file));
        }
        foreach (var file in EnumerateDir(OfflineDictEngine.DictDir, "*.txt"))
        {
            AddFile(zip, file, "词典/" + Path.GetFileName(file));
        }
        return targetZip;
    }

    /// <summary>导入配置包（覆盖同名文件，不改动包外的其他词库）。</summary>
    public static ImportSummary ImportSettings(string zipPath)
    {
        int settings = 0, favorites = 0, banks = 0, dicts = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

            if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(Path.Combine(SettingsStore.DataDir, "settings.json"), true);
                settings++;
            }
            else if (name.Equals("favorites.json", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(Path.Combine(SettingsStore.DataDir, "favorites.json"), true);
                favorites++;
            }
            else if (name.StartsWith("词库" + Path.DirectorySeparatorChar))
            {
                Directory.CreateDirectory(QuoteRepository.BankDir);
                entry.ExtractToFile(Path.Combine(QuoteRepository.BankDir, entry.Name), true);
                banks++;
            }
            else if (name.StartsWith("词典" + Path.DirectorySeparatorChar))
            {
                Directory.CreateDirectory(OfflineDictEngine.DictDir);
                entry.ExtractToFile(Path.Combine(OfflineDictEngine.DictDir, entry.Name), true);
                dicts++;
            }
        }
        return new ImportSummary(settings, favorites, banks, dicts);
    }

    /// <summary>只导出词库（分享给朋友用），附一份放置说明。</summary>
    public static int ExportBankPack(string targetZip)
    {
        using var zip = ZipFile.Open(targetZip, ZipArchiveMode.Create);
        int count = 0;
        foreach (var file in EnumerateDir(QuoteRepository.BankDir, "*.md")
                     .Concat(EnumerateDir(QuoteRepository.BankDir, "*.txt")))
        {
            if (Path.GetFileName(file).StartsWith("README", StringComparison.OrdinalIgnoreCase)) continue;
            AddFile(zip, file, Path.GetFileName(file));
            count++;
        }

        var readme = zip.CreateEntry("使用说明.txt");
        using var writer = new StreamWriter(readme.Open(), new UTF8Encoding(false));
        writer.WriteLine("拾句 · 词库包");
        writer.WriteLine("================");
        writer.WriteLine();
        writer.WriteLine("把本压缩包里的 .md / .txt 文件放到 拾句 程序目录的「词库」文件夹里，");
        writer.WriteLine("然后在 设置 → 内容 里点「刷新词库」即可使用（也可以直接替换内置分类文件）。");
        writer.WriteLine();
        writer.WriteLine("格式：每行一句，—— 后为出处；行首缩进的行会并入上一句（适合英中对照）。");
        return count;
    }

    private static IEnumerable<string> EnumerateDir(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, pattern)) yield return f;
    }

    private static void AddFile(ZipArchive zip, string path, string entryName)
    {
        if (!File.Exists(path)) return;
        zip.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
    }
}
