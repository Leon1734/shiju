using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

public static class SettingsStore
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuoteWidget");

    private static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        AppSettings? loaded = null;
        try
        {
            if (File.Exists(FilePath))
                loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
        }
        catch { /* 配置损坏时回退默认值 */ }
        loaded ??= new AppSettings();

        // v2 配置默认半透明卡片，用户反馈边框突兀：v3 起默认全透明纯文字
        if (loaded.SettingsVersion < 3)
        {
            loaded.BgMode = BackgroundMode.Transparent;
            loaded.SettingsVersion = 3;
            Save(loaded);
        }
        return loaded;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch { /* 保存失败不打断运行，下次改动再试 */ }
    }
}
