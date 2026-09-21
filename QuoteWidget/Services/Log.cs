using System.IO;

namespace QuoteWidget.Services;

/// <summary>极简文件日志：%APPDATA%\QuoteWidget\logs\app-日期.log，自动清理 7 天前的旧日志。</summary>
public static class Log
{
    private static readonly object Lock = new();

    private static string Dir => Path.Combine(SettingsStore.DataDir, "logs");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : message + " :: " + ex);

    public static void Cleanup(int keepDays = 7)
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(Dir, "app-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file); // app-20260918
                if (name.Length >= 11 && DateTime.TryParseExact(name[4..], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var date) && date < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch { }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(
                    Path.Combine(Dir, $"app-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
