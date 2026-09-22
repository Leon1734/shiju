using QuoteWidget.Services;
Console.WriteLine($"QUNS 原始状态 = {FullscreenWatcher.QueryState()} (仅 3/4 会直接触发隐藏)");
Console.WriteLine($"前台窗口是否真全屏 = {FullscreenWatcher.IsForegroundTrulyFullscreen()} (期望 False)");
Console.WriteLine($"最终是否应隐藏 = {FullscreenWatcher.ShouldHideWidget()} (期望 False)");
