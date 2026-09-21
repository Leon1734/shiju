using QuoteWidget.Services;
var dir = @"E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\bin\Release\net10.0-windows\win-x64\词库";
int total = 0;
foreach (var f in Directory.GetFiles(dir).OrderBy(x => x))
{
    var issues = QuoteRepository.AnalyzeBankFile(f);
    var count = QuoteRepository.ParseBankFile(f, "count").Count;
    total += count;
    Console.WriteLine($"{Path.GetFileName(f)}: {count} 句, {issues.Count} 个问题");
    foreach (var i in issues.Take(8)) Console.WriteLine($"   [{i.Line}] {i.Issue}");
}
Console.WriteLine($"TOTAL: {total} 句");
