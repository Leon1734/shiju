using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuoteWidget.Models;

namespace QuoteWidget.Services;

/// <summary>
/// 分享图生成：把当前句子渲染成 1280×720 的精美卡片 PNG，
/// 自动存到 图片\拾句\ 并复制到剪贴板。
/// </summary>
public static class ShareCardService
{
    private const double W = 1280, H = 720;

    public static string SaveAndCopy(Quote quote, AppSettings settings)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "拾句");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"拾句_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        var bitmap = Render(quote, settings);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);

        try { Clipboard.SetImage(bitmap); }
        catch { /* 剪贴板偶发占用：文件已保存，不影响 */ }

        return path;
    }

    private static BitmapSource Render(Quote quote, AppSettings settings)
    {
        bool transparent = settings.BgMode == BackgroundMode.Transparent;
        var baseColor = transparent
            ? Color.FromRgb(0x2D, 0x2D, 0x3A)
            : ParseColor(settings.BgColor, "#2D2D3A");

        double lum = (0.299 * baseColor.R + 0.587 * baseColor.G + 0.114 * baseColor.B) / 255.0;
        bool darkBg = lum < 0.5;
        var textColor = darkBg ? Color.FromRgb(0xF5, 0xF1, 0xE8) : Color.FromRgb(0x2D, 0x34, 0x36);
        var faint = Color.FromArgb(0xB3, textColor.R, textColor.G, textColor.B);

        Color Shift(Color c, double amount)
        {
            byte F(byte v) => (byte)Math.Clamp(amount > 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
            return Color.FromArgb(0xFF, F(c.R), F(c.G), F(c.B));
        }

        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var lightTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 背景渐变
            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            bg.GradientStops.Add(new GradientStop(Shift(baseColor, 0.16), 0));
            bg.GradientStops.Add(new GradientStop(baseColor, 0.55));
            bg.GradientStops.Add(new GradientStop(Shift(baseColor, -0.2), 1));
            dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));

            // 左上角装饰引号
            var glyph = new FormattedText("\u201C", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                300, new SolidColorBrush(Color.FromArgb(0x2E, textColor.R, textColor.G, textColor.B)), 1.0);
            dc.DrawText(glyph, new Point(56, 8));

            // 正文（自动换行 + 居中，多行句的 \n 原样生效）
            var body = new FormattedText(quote.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 46,
                new SolidColorBrush(textColor), 1.0)
            {
                MaxTextWidth = W - 240,
                MaxTextHeight = H - 260,
                TextAlignment = TextAlignment.Center,
                LineHeight = 72
            };
            dc.DrawText(body, new Point(120, (H - body.Height) / 2 - 34));

            // 出处（右下）
            if (!string.IsNullOrEmpty(quote.Source))
            {
                var source = new FormattedText("—— " + quote.Source, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, lightTypeface, 26,
                    new SolidColorBrush(faint), 1.0);
                dc.DrawText(source, new Point(W - 120 - source.Width, (H + body.Height) / 2 + 18));
            }

            // 页脚：分类 + 日期
            var footerLeft = new FormattedText($"拾句 · {quote.CategoryName}", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, lightTypeface, 20,
                new SolidColorBrush(Color.FromArgb(0x80, textColor.R, textColor.G, textColor.B)), 1.0);
            dc.DrawText(footerLeft, new Point(64, H - 62));

            var footerRight = new FormattedText(DateTime.Now.ToString("yyyy 年 M 月 d 日"), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, lightTypeface, 20,
                new SolidColorBrush(Color.FromArgb(0x80, textColor.R, textColor.G, textColor.B)), 1.0);
            dc.DrawText(footerRight, new Point(W - 64 - footerRight.Width, H - 62));
        }

        var bitmap = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color ParseColor(string hex, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }
}
