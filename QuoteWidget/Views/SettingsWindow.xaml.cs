using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuoteWidget.Models;
using QuoteWidget.Services;

namespace QuoteWidget.Views;

public partial class SettingsWindow : Window
{
    private static readonly string[] BgPresets =
    {
        "#2D2D3A", "#1E1E2A", "#22303F", "#2F4F4F", "#3A2E28",
        "#6C5CE7", "#E17055", "#00B894", "#F5F1E8", "#FFFFFF"
    };


    private static readonly string[] TextPresets =
    {
        "#FFFFFF", "#F5F1E8", "#FFEAA7", "#FF7675",
        "#74B9FF", "#55EFC4", "#2D3436", "#6C5CE7"
    };

    private static readonly string[] FontChoices =
    {
        "Microsoft YaHei UI", "微软雅黑", "等线", "黑体", "楷体",
        "仿宋", "宋体", "Segoe UI", "Georgia"
    };

    /// <summary>主题预设：一键套用一组观感配置。</summary>
    private static readonly (string Name, Action<AppSettings> Apply)[] ThemePresets =
    {
        ("墨夜", s => { s.BgMode = BackgroundMode.Translucent; s.BgColor = "#1E1E2A"; s.BgOpacity = 0.65;
                       s.TextColor = "#F5F1E8"; s.CornerRadius = 16; s.Bold = false; s.AdaptiveTextColor = false; }),
        ("纸白", s => { s.BgMode = BackgroundMode.Solid; s.BgColor = "#F7F3EA"; s.BgOpacity = 0.95;
                       s.TextColor = "#3A3630"; s.CornerRadius = 12; s.Bold = false; s.AdaptiveTextColor = false; }),
        ("全透·白字", s => { s.BgMode = BackgroundMode.Transparent; s.TextColor = "#FFFFFF"; s.AdaptiveTextColor = false; }),
        ("全透·墨字", s => { s.BgMode = BackgroundMode.Transparent; s.TextColor = "#2D3436"; s.AdaptiveTextColor = false; }),
        ("青瓷", s => { s.BgMode = BackgroundMode.Translucent; s.BgColor = "#2F5D62"; s.BgOpacity = 0.5;
                       s.TextColor = "#EAF4F4"; s.CornerRadius = 18; s.Bold = false; s.AdaptiveTextColor = false; }),
        ("樱粉", s => { s.BgMode = BackgroundMode.Translucent; s.BgColor = "#F8E0E7"; s.BgOpacity = 0.85;
                       s.TextColor = "#6D3B52"; s.CornerRadius = 20; s.Bold = false; s.AdaptiveTextColor = false; }),
        ("智能跟随壁纸", s => { s.BgMode = BackgroundMode.Transparent; s.AdaptiveTextColor = true; })
    };

    private readonly AppSettings _settings;

    /// <summary>用 record（属性）而非元组（字段）：WPF 的 DisplayMemberPath/SelectedValuePath 取不到元组字段。</summary>
    private sealed record BgModeItem(BackgroundMode Value, string Label);
    private sealed record ComboItem(int Value, string Label);

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = AppServices.Settings;
        DataContext = _settings;

        BgModeCombo.ItemsSource = new List<BgModeItem>
        {
            new(BackgroundMode.Transparent, "全透明（只显示文字）"),
            new(BackgroundMode.Solid, "纯色卡片"),
            new(BackgroundMode.Translucent, "半透明卡片")
        };
        BgModeCombo.DisplayMemberPath = nameof(BgModeItem.Label);
        BgModeCombo.SelectedValuePath = nameof(BgModeItem.Value);
        BgModeCombo.SelectedValue = _settings.BgMode; // 绑定建立时还没有 ItemsSource，这里显式补上选中项

        FontCombo.ItemsSource = FontChoices;

        BuildSwatches(BgSwatches, BgPresets, hex => _settings.BgColor = hex);
        BuildSwatches(TextSwatches, TextPresets, hex => _settings.TextColor = hex);

        BuildThemePresets();
        BuildDndCombos();
        BuildScheduleCombos();
        BuildEngineCombo();
        BuildAutostartDelayCombo();
        InitHotkeyBoxes();
        ApplyEngineRows();

        RebuildCustomBanks();
        ApplyBgControlsEnabled();
        ApplyDailyModeEnabled();
        RefreshPoolCount();

        _settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppSettings.CatMovie) or nameof(AppSettings.CatGame)
                or nameof(AppSettings.CatNovel) or nameof(AppSettings.CatLyric)
                or nameof(AppSettings.UseHitokoto))
            {
                RefreshPoolCount();
            }
            else if (args.PropertyName == nameof(AppSettings.BgMode))
            {
                ApplyBgControlsEnabled();
            }
            else if (args.PropertyName == nameof(AppSettings.DailyMode))
            {
                ApplyDailyModeEnabled();
            }
        };
    }

    // ———————— v2 设置项 ————————

    private void BuildThemePresets()
    {
        foreach (var (name, apply) in ThemePresets)
        {
            var button = new Button
            {
                Content = name,
                Style = TryFindResource("DialogButton") as Style,
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) =>
            {
                apply(_settings);
                ApplyBgControlsEnabled();
                Log.Info("theme preset applied: " + name);
            };
            ThemePresetsPanel.Children.Add(button);
        }
    }

    private void BuildDndCombos()
    {
        var hours = Enumerable.Range(0, 24).ToList();
        DndStartCombo.ItemsSource = hours;
        DndEndCombo.ItemsSource = hours;
        DndStartCombo.SelectedItem = _settings.DndStartHour;
        DndEndCombo.SelectedItem = _settings.DndEndHour;
        DndStartCombo.SelectionChanged += (_, _) =>
        {
            if (DndStartCombo.SelectedItem is int h) _settings.DndStartHour = h;
        };
        DndEndCombo.SelectionChanged += (_, _) =>
        {
            if (DndEndCombo.SelectedItem is int h) _settings.DndEndHour = h;
        };
    }

    private void ApplyDailyModeEnabled()
    {
        // 每日一句模式下，自动换句间隔由"跨零点"接管，不再有意义
        IntervalSlider.IsEnabled = !_settings.DailyMode;
    }

    private void BuildAutostartDelayCombo()
    {
        AutostartDelayCombo.ItemsSource = new List<ComboItem>
        {
            new(0, "不延迟"), new(15, "15 秒"), new(30, "30 秒"), new(60, "60 秒")
        };
        AutostartDelayCombo.DisplayMemberPath = nameof(ComboItem.Label);
        AutostartDelayCombo.SelectedValuePath = nameof(ComboItem.Value);
        AutostartDelayCombo.SelectedValue = _settings.AutostartDelaySeconds;
        AutostartDelayCombo.SelectionChanged += (_, _) =>
        {
            if (AutostartDelayCombo.SelectedValue is int v) _settings.AutostartDelaySeconds = v;
        };
    }

    // ———————— v2.1 定时词库 / 体检 ————————

    private void BuildScheduleCombos()
    {
        SetupSchedule(SchedMovie, "电影台词");
        SetupSchedule(SchedGame, "游戏台词");
        SetupSchedule(SchedNovel, "小说名句");
        SetupSchedule(SchedLyric, "音乐歌词");
    }

    private void SetupSchedule(ComboBox combo, string bankName)
    {
        combo.ItemsSource = new List<ComboItem>
        {
            new(0, "全天"), new(1, "白天 8-22 点"), new(2, "夜间 22-8 点")
        };
        combo.DisplayMemberPath = nameof(ComboItem.Label);
        combo.SelectedValuePath = nameof(ComboItem.Value);
        combo.SelectedValue = _settings.BankSchedules.TryGetValue(bankName, out var v) ? v : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedValue is int value)
            {
                _settings.BankSchedules[bankName] = value;
                AppServices.Quotes.Reload(_settings);
                SettingsStore.Save(_settings);
            }
        };
    }

    private void OnBankHealth(object sender, RoutedEventArgs e)
    {
        try { new BankHealthWindow { Owner = this }.Show(); }
        catch (Exception ex) { Log.Error("打开词库体检失败", ex); }
    }

    // ———————— v2.2 翻译 ————————

    private void BuildEngineCombo()
    {
        TranslateEngineCombo.ItemsSource = LlmEngine.Presets
            .Select(p => new EngineKindItem(p.Kind, p.Name)).ToList();
        TranslateEngineCombo.DisplayMemberPath = nameof(EngineKindItem.Label);
        TranslateEngineCombo.SelectedValuePath = nameof(LlmEngine.EnginePreset.Kind);
        TranslateEngineCombo.SelectedValue = _settings.TranslateEngine;
        TranslateEngineCombo.SelectionChanged += (_, _) => ApplyEngineRows();
    }

    private sealed record EngineKindItem(string Kind, string Label);

    /// <summary>按所选引擎显示 Key/地址/模型 三行（预填官方推荐值）。</summary>
    private void ApplyEngineRows()
    {
        var kind = TranslateEngineCombo.SelectedValue as string ?? "OfflineDict";
        if (kind != _settings.TranslateEngine) _settings.TranslateEngine = kind;

        if (kind == "OfflineDict")
        {
            EngKeyRow.Visibility = Visibility.Collapsed;
            EngUrlRow.Visibility = Visibility.Collapsed;
            EngModelRow.Visibility = Visibility.Collapsed;
            EngineHintText.Text = "离线查词：把 ecdict-compact.txt 放入程序目录的「词典」文件夹（格式：词 Tab 音标 Tab 词性 Tab 释义，可自行增补），右侧按钮可打开该文件夹。";
            return;
        }

        var preset = LlmEngine.GetPreset(kind);
        if (preset == null) return;
        var (url, model) = LlmEngine.Resolve(kind, _settings);

        EngKeyLabel.Text = preset.Name.Split('（')[0].Trim() + " Key";
        EngKeyBox.Text = LlmEngine.GetApiKey(kind, _settings);
        EngUrlBox.Text = url;
        EngModelBox.Text = model;
        EngKeyRow.Visibility = preset.RequiresKey ? Visibility.Visible : Visibility.Collapsed;
        EngUrlRow.Visibility = Visibility.Visible;
        EngModelRow.Visibility = Visibility.Visible;

        EngineHintText.Text = preset.RequiresKey
            ? "地址与模型已按官方推荐预填，可改（如 DeepSeek 也可换 deepseek-reasoner）。Key 仅保存在本机 settings.json。"
            : "本地引擎无需 Key：确保已安装 Ollama 并已拉取模型（ollama pull " + preset.DefaultModel + "）。";
    }

    private void OnEngineKeyLostFocus(object sender, RoutedEventArgs e)
    {
        var kind = TranslateEngineCombo.SelectedValue as string;
        if (string.IsNullOrEmpty(kind)) return;
        LlmEngine.SetApiKey(kind, _settings, EngKeyBox.Text.Trim());
        SettingsStore.Save(_settings);
    }

    private void OnEngineUrlModelLostFocus(object sender, RoutedEventArgs e)
    {
        var kind = TranslateEngineCombo.SelectedValue as string;
        var preset = LlmEngine.GetPreset(kind ?? "");
        if (kind == null || preset == null) return;

        void Set(IDictionary<string, string> dict, string value, string? defaultValue)
        {
            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value) || value == defaultValue) dict.Remove(kind);
            else dict[kind] = value;
        }
        Set(_settings.EngineUrls, EngUrlBox.Text, preset.DefaultUrl);
        Set(_settings.EngineModels, EngModelBox.Text, preset.DefaultModel);
        SettingsStore.Save(_settings);
    }

    /// <summary>从翻译小窗跳转过来时，滚动到翻译引擎分组并高亮一下。</summary>
    public void FocusTranslationSection()
    {
        TranslateGroupBox.BringIntoView();
        TranslateGroupBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xEF));
        TranslateGroupBox.BorderThickness = new Thickness(2);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            TranslateGroupBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xE0));
            TranslateGroupBox.BorderThickness = new Thickness(1);
            timer.Stop();
        };
        timer.Start();
    }

    // ———————— 设置搜索 ————————

    /// <summary>关键词过滤：匹配的分组/行保留，其余折叠；清空恢复。</summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = (sender as TextBox)?.Text.Trim() ?? "";
        bool any = true;
        foreach (var groupBox in GetDescendants<GroupBox>(ContentStack))
        {
            if (groupBox.Content is not Panel panel) continue;
            bool groupVisible = query.Length == 0;
            var rows = panel is Grid grid
                ? grid.Children.Cast<UIElement>()
                    .GroupBy(Grid.GetRow)
                    .Select(g => g.ToList())
                : panel.Children.Cast<UIElement>().Select(c => new List<UIElement> { c });

            foreach (var row in rows)
            {
                bool rowVisible = query.Length == 0 ||
                                  row.Any(el => TextOf(el).Contains(query, StringComparison.OrdinalIgnoreCase));
                foreach (var el in row) el.Visibility = rowVisible ? Visibility.Visible : Visibility.Collapsed;
                if (rowVisible) groupVisible = true;
            }
            groupBox.Visibility = groupVisible ? Visibility.Visible : Visibility.Collapsed;
            any &= groupVisible;
        }
        if (query.Length == 0)
        {
            // 清空搜索后恢复各联动行的显隐规则
            ApplyEngineRows();
            ApplyBgControlsEnabled();
        }
        _ = any;
    }

    private static string TextOf(UIElement element)
    {
        var sb = new System.Text.StringBuilder();
        Walk(element);
        return sb.ToString();

        void Walk(DependencyObject node)
        {
            switch (node)
            {
                case TextBlock tb: sb.Append(tb.Text).Append(' '); break;
                case TextBox box: sb.Append(box.Text).Append(' '); break;
                case ContentControl { Content: string content }: sb.Append(content).Append(' '); break;
                case FrameworkElement { ToolTip: string tip }: sb.Append(tip).Append(' '); break;
            }
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }
    }

    private static IEnumerable<T> GetDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T matched) yield return matched;
            foreach (var sub in GetDescendants<T>(child))
                yield return sub;
        }
    }

    // ———————— 软件升级 ————————

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.UpdateUrl))
        {
            UpdateStatusText.Text = "未配置更新地址。把更新清单 JSON 的 URL 填到上方「更新检查地址」即可启用。";
            return;
        }
        UpdateStatusText.Text = "正在检查更新…";
        var info = await UpdateChecker.CheckAsync(_settings.UpdateUrl);
        if (info == null)
        {
            UpdateStatusText.Text = "已是最新版本，或更新地址暂不可访问。";
            return;
        }
        Log.Info($"update: 发现新版本 {info.LatestVersion}");
        if (!string.IsNullOrWhiteSpace(info.FileUrl))
        {
            var go = MessageBox.Show(
                $"发现新版本 v{info.LatestVersion}\n\n{info.Notes}\n\n立即下载并升级？升级会自动重启程序。",
                "拾句 · 软件升级", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (go != MessageBoxResult.Yes) return;
            UpdateStatusText.Text = "正在下载升级包…";
            try
            {
                var path = await UpdateService.DownloadAsync(info);
                UpdateService.ApplyAndRestart(path);
            }
            catch (Exception ex)
            {
                Log.Error("update: 升级失败", ex);
                UpdateStatusText.Text = "升级失败：" + ex.Message;
            }
        }
        else
        {
            UpdateStatusText.Text = $"发现新版本 v{info.LatestVersion}（清单未提供直链，打开下载页手动获取）。";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = info.DownloadUrl, UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OnManualUpgrade(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择升级包（zip 或 exe）",
            Filter = "升级包|QuoteWidget*.zip;QuoteWidget*.exe|所有支持的文件|*.zip;*.exe"
        };
        if (dialog.ShowDialog(this) != true) return;
        var confirm = MessageBox.Show(
            "将用所选包升级并自动重启程序，继续？\n（词库与设置数据不受影响）",
            "拾句 · 手动升级", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            UpdateService.ApplyLocalPackage(dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("update: 手动升级失败", ex);
            MessageBox.Show("升级失败：" + ex.Message, "拾句", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnOpenDictFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(OfflineDictEngine.DictDir);
            Process.Start(new ProcessStartInfo { FileName = OfflineDictEngine.DictDir, UseShellExecute = true });
        }
        catch { }
    }

    private void InitHotkeyBoxes()
    {
        HotkeySwitchBox.Text = _settings.HotkeySwitch;
        HotkeyFavBox.Text = _settings.HotkeyFavorite;
        HotkeyToggleBox.Text = _settings.HotkeyToggle;
    }

    /// <summary>快捷键录入：点击输入框后直接按组合键；Esc/退格清除。</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return; // 只按了修饰键，等完整组合
        }

        var box = (TextBox)sender;
        if (key is Key.Escape or Key.Back or Key.Delete)
        {
            box.Text = "";
            SyncHotkeySetting(box);
            return;
        }

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        box.Text = string.Join("+", parts);
        SyncHotkeySetting(box);
    }

    private void SyncHotkeySetting(TextBox box)
    {
        switch (box.Name)
        {
            case nameof(HotkeySwitchBox): _settings.HotkeySwitch = box.Text; break;
            case nameof(HotkeyFavBox): _settings.HotkeyFavorite = box.Text; break;
            case nameof(HotkeyToggleBox): _settings.HotkeyToggle = box.Text; break;
        }
    }

    // ———————— 外观 ————————

    private void BuildSwatches(Panel panel, string[] presets, Action<string> apply)
    {
        foreach (var hex in presets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };
            swatch.MouseLeftButtonDown += (_, _) => apply(hex);
            panel.Children.Add(swatch);
        }
    }

    /// <summary>全透明模式下没有背景，颜色/不透明度/圆角设置置灰避免"调了没反应"。</summary>
    private void ApplyBgControlsEnabled()
    {
        bool enabled = _settings.BgMode != BackgroundMode.Transparent;
        BgColorRow.IsEnabled = enabled;
        BgOpacityRow.IsEnabled = enabled;
        CornerRadiusRow.IsEnabled = enabled;
        BgOpacityHintText.Text = enabled ? "" : "全透明模式没有背景，颜色 / 不透明度 / 圆角不生效；切到「半透明卡片」即可调整。";
    }

    // ———————— 内容 ————————

    private void RefreshPoolCount()
    {
        var quotes = AppServices.Quotes;
        PoolCountText.Text =
            $"全部词库：共 {quotes.TotalCount} 条，当前启用 {quotes.PoolCount} 条"
            + (_settings.UseHitokoto ? "，另联网补充「一言」" : "");
    }

    /// <summary>重新扫描词库文件夹并生成开关列表（内置四类文件由固定开关控制，不在此列出）。</summary>
    private void RebuildCustomBanks()
    {
        CustomBanksPanel.Children.Clear();
        var files = ListCustomBankFiles().ToList();

        if (files.Count == 0)
        {
            CustomBanksPanel.Children.Add(new TextBlock
            {
                Text = "还没有自定义词库。点「打开词库文件夹」，放入 .md 或 .txt 文件（每行一句，可用——标出处），再点「刷新词库」。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x93)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            return;
        }

        var style = TryFindResource("RowCheck") as Style;
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            bool enabled = !_settings.CustomCategories.TryGetValue(name, out var on) || on;
            var row = new DockPanel();
            var checkBox = new CheckBox
            {
                Content = $"{name}（{CountBankFile(file)} 句）",
                IsChecked = enabled,
                Style = style
            };
            checkBox.Checked += (_, _) => ToggleCustomBank(name, true);
            checkBox.Unchecked += (_, _) => ToggleCustomBank(name, false);
            row.Children.Add(checkBox);

            // 定时词库时段
            var schedule = new ComboBox { Width = 76, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            schedule.ItemsSource = new List<ComboItem>
            {
                new(0, "全天"), new(1, "白天 8-22 点"), new(2, "夜间 22-8 点")
            };
            schedule.DisplayMemberPath = nameof(ComboItem.Label);
            schedule.SelectedValuePath = nameof(ComboItem.Value);
            schedule.SelectedValue = _settings.BankSchedules.TryGetValue(name, out var sc) ? sc : 0;
            schedule.SelectionChanged += (_, _) =>
            {
                if (schedule.SelectedValue is int value)
                {
                    _settings.BankSchedules[name] = value;
                    AppServices.Quotes.Reload(_settings);
                    SettingsStore.Save(_settings);
                }
            };
            row.Children.Add(schedule);
            row.Margin = new Thickness(0, 0, 0, 2);
            CustomBanksPanel.Children.Add(row);
        }
    }

    private void ToggleCustomBank(string name, bool enabled)
    {
        _settings.CustomCategories[name] = enabled;
        AppServices.Quotes.Reload(_settings);
        SettingsStore.Save(_settings);
        RefreshPoolCount();
    }

    private static IEnumerable<string> ListCustomBankFiles()
    {
        if (!Directory.Exists(QuoteRepository.BankDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(QuoteRepository.BankDir)
                     .Where(f => (f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                               || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                              && !QuoteRepository.IsBuiltInBankFile(f)
                              && !Path.GetFileName(f).StartsWith("readme", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.CurrentCulture))
        {
            yield return file;
        }
    }

    /// <summary>用解析器统计句数（多行句合并计 1，避免英中对照被算成两句）。</summary>
    private static int CountBankFile(string file)
    {
        try
        {
            return QuoteRepository.ParseBankFile(file, "count").Count;
        }
        catch
        {
            return 0;
        }
    }

    private void OnOpenBankFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(QuoteRepository.BankDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = QuoteRepository.BankDir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnRefreshBanks(object sender, RoutedEventArgs e)
    {
        RebuildCustomBanks();
        AppServices.Quotes.Reload(_settings);
        RefreshPoolCount();
    }

    // ———————— 通用 ————————

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SettingsStore.DataDir, UseShellExecute = true });
        }
        catch { }
    }

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        RebuildCustomBanks();
        RefreshPoolCount();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        SettingsStore.Save(_settings);
        base.OnClosed(e);
    }
}
