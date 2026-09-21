// 业务逻辑自测：收藏落盘、开机自启注册表、词库文件解析（执行后自动还原，不留痕迹）
using QuoteWidget.Models;
using QuoteWidget.Services;

var store = new FavoritesStore();
store.Load();
int before = store.Items.Count;

var q = new Quote { Text = "测试句子：验证收藏落盘", Source = "《自动化测试》", Category = "movie" };

bool added = store.Toggle(q);
Console.WriteLine($"favorite added={added}, count={store.Items.Count} (before={before})");
bool removed = store.Toggle(q);
Console.WriteLine($"favorite removed={removed}, count={store.Items.Count}");

AutoStartService.Set(true);
bool on = AutoStartService.IsEnabled();
AutoStartService.Set(false);
bool off = AutoStartService.IsEnabled();
Console.WriteLine($"autostart on={on}, off={off}");

// 词库文件解析验证：解析用户正在用的 win-x64\词库\英文好句.md
var bankDir = @"E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\bin\Release\net10.0-windows\win-x64\词库";
var english = Path.Combine(bankDir, "英文好句.md");
if (File.Exists(english))
{
    var parsed = QuoteRepository.ParseBankFile(english, "test");
    Console.WriteLine($"英文好句.md parsed -> {parsed.Count} quotes:");
    foreach (var p in parsed.Take(5))
        Console.WriteLine($"  ---\n  text=[{p.Text.Replace("\n", " / ")}]  source=[{p.Source}]");
}
else
{
    Console.WriteLine($"(未找到 {english})");
}

var s = new AppSettings();
Console.WriteLine($"settings defaults: mode={s.BgMode}, interval={s.AutoSwitchSeconds}s, hitokoto={s.UseHitokoto}");
Console.WriteLine("ALL TESTS DONE");
