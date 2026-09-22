using System.Runtime.InteropServices;
using QuoteWidget.Services;

[DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
Console.WriteLine($"QUNS 原始状态 = {SHQueryUserNotificationState(out int st)} / state={st}");
Console.WriteLine("(1=锁屏/屏保 2=全屏应用或演示 3=D3D独占 4=演示模式 5=正常 6=专注 7=App)");

// 在 harness 自己的目录里造一个词库文件夹再导出，验证打包逻辑
var bankDir = Path.Combine(AppContext.BaseDirectory, "词库");
Directory.CreateDirectory(bankDir);
File.WriteAllText(Path.Combine(bankDir, "测试词库.md"), "1. 测试句子。——《测试》\n");
var tmp = Path.Combine(Path.GetTempPath(), "shiju_cfg_test.zip");
ConfigPackService.ExportSettings(tmp);
using (var zip = System.IO.Compression.ZipFile.OpenRead(tmp))
{
    Console.WriteLine($"配置包条目 = {zip.Entries.Count}: " + string.Join(", ", zip.Entries.Select(e => e.FullName)));
}
