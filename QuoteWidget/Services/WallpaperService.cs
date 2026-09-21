using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace QuoteWidget.Services;

/// <summary>
/// 桌面壁纸平均亮度（0~1）：用于全透明模式下自动选黑字/白字。
/// 启动时后台采样；壁纸更换（UserPreferenceChanged）时自动重新采样。
/// </summary>
public static class WallpaperService
{
    /// <summary>当前壁纸平均亮度（0=纯黑 1=纯白）；采样失败按 0.3（偏暗）处理。</summary>
    public static double Luminance { get; private set; } = 0.3;

    /// <summary>亮度更新完成（UI 线程回调）。</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        Task.Run(() =>
        {
            Sample();
            SystemEvents.UserPreferenceChanged += (_, args) =>
            {
                if (args.Category == UserPreferenceCategory.Desktop)
                    Task.Run(Sample);
            };
        });
    }

    private static void Sample()
    {
        try
        {
            var path = GetWallpaperPath();
            if (path == null)
            {
                Log.Warn("wallpaper: 未找到壁纸文件");
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 32; // 缩到 32 宽采样，省内存
            image.EndInit();

            int width = image.PixelWidth, height = image.PixelHeight;
            if (width == 0 || height == 0) return;
            var pixels = new byte[width * height * 4];
            image.CopyPixels(pixels, width * 4, 0);

            double sum = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                sum += (0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i]) / 255.0;

            Luminance = sum / (width * height);
            Log.Info($"wallpaper: 亮度={Luminance:F2} ({path})");
            Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
        }
        catch (Exception ex)
        {
            Log.Error("wallpaper: 采样失败", ex);
        }
    }

    private static string? GetWallpaperPath()
    {
        var reg = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
        if (!string.IsNullOrWhiteSpace(reg) && File.Exists(reg)) return reg;

        // 幻灯片壁纸 / 系统转码后的壁纸（无扩展名的 JPEG）
        var transcoded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }
}
