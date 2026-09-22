using QuoteWidget.Services;

Console.WriteLine("=== 测试 GitHub 直连（DoH 绕过 hosts 拦截）===");
var rel = await UpdateChecker.CheckGitHubReleaseAsync("Leon1734/shiju");
Console.WriteLine(rel == null
    ? "[Release] 无新版本（当前 2.5.0 > GitHub 上 v2.4.3）✓ 或检查失败 ✗"
    : $"[Release] 发现新版本 v{rel.LatestVersion}，zip 直链={(rel.FileUrl.Length > 0 ? "有" : "无")}");

var commit = await UpdateChecker.GetLatestCommitAsync("Leon1734/shiju");
Console.WriteLine(commit == null
    ? "[Commit] 检查失败 ✗"
    : $"[Commit] 最新提交 {commit.ShortSha}：{commit.Message}（{commit.Date}）");
Console.WriteLine("=== 完 ===");
