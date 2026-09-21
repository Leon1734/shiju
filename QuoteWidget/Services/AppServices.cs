using System.IO;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>轻量服务定位器：应用启动时初始化一次，全局共享。</summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();
    public static QuoteRepository Quotes { get; } = new();
    public static HitokotoService Hitokoto { get; } = new();
    public static FavoritesStore Favorites { get; } = new();

    public static void Initialize()
    {
        Settings = SettingsStore.Load();
        MigrateOldCustomBanks(); // 旧版本词库存于 %APPDATA%，搬到程序目录
        MigrateLegacyEngineConfig();
        Quotes.LoadFromResource();
        Quotes.Reload(Settings);
        Favorites.Load();
    }

    /// <summary>旧版分散的引擎模型/地址字段迁移到统一的字典配置。</summary>
    private static void MigrateLegacyEngineConfig()
    {
        var s = Settings;
        if (!s.EngineModels.ContainsKey("SiliconFlow") && !string.IsNullOrWhiteSpace(s.SiliconModel))
            s.EngineModels["SiliconFlow"] = s.SiliconModel;
        if (!s.EngineModels.ContainsKey("Ollama") && !string.IsNullOrWhiteSpace(s.OllamaModel))
            s.EngineModels["Ollama"] = s.OllamaModel;
        if (!s.EngineUrls.ContainsKey("Custom") && !string.IsNullOrWhiteSpace(s.CustomBaseUrl))
            s.EngineUrls["Custom"] = s.CustomBaseUrl;
        if (!s.EngineModels.ContainsKey("Custom") && !string.IsNullOrWhiteSpace(s.CustomModel))
            s.EngineModels["Custom"] = s.CustomModel;
    }

    /// <summary>把旧版 %APPDATA%\QuoteWidget\词库\ 里的文件搬到程序目录词库文件夹。</summary>
    private static void MigrateOldCustomBanks()
    {
        var oldDir = Path.Combine(SettingsStore.DataDir, "词库");
        if (!Directory.Exists(oldDir)) return;
        try
        {
            Directory.CreateDirectory(QuoteRepository.BankDir);
            foreach (var file in Directory.EnumerateFiles(oldDir))
            {
                var dest = Path.Combine(QuoteRepository.BankDir, Path.GetFileName(file));
                if (!File.Exists(dest)) File.Copy(file, dest);
            }
        }
        catch { }
    }
}
