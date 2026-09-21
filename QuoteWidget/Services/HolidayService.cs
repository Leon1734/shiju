namespace QuoteWidget.Services;

/// <summary>
/// 节日识别：公历节日全年可用；农历节日（春节/除夕/元宵/端午/中秋）
/// 只内置了已核实的 2025-2026 两年，之后逐年补充，未收录的年份自动只按公历识别。
/// </summary>
public static class HolidayService
{
    private static readonly Dictionary<string, string> LunarHolidays = new()
    {
        ["2025-01-28"] = "除夕",
        ["2025-01-29"] = "春节",
        ["2025-02-12"] = "元宵节",
        ["2025-05-31"] = "端午节",
        ["2025-08-29"] = "七夕",
        ["2025-10-06"] = "中秋节",
        ["2026-02-16"] = "除夕",
        ["2026-02-17"] = "春节",
        ["2026-03-03"] = "元宵节",
        ["2026-06-19"] = "端午节",
        ["2026-09-25"] = "中秋节",
    };

    private static readonly (int Month, int Day, string Name)[] SolarHolidays =
    {
        (1, 1, "元旦"),
        (2, 14, "情人节"),
        (3, 8, "妇女节"),
        (5, 1, "劳动节"),
        (5, 4, "青年节"),
        (6, 1, "儿童节"),
        (9, 10, "教师节"),
        (10, 1, "国庆节"),
        (12, 25, "圣诞节"),
    };

    /// <summary>返回当天节日名；不是节日返回 null。</summary>
    public static string? GetToday(DateTime? date = null)
    {
        var d = (date ?? DateTime.Now).Date;
        foreach (var (month, day, name) in SolarHolidays)
        {
            if (d.Month == month && d.Day == day) return name;
        }
        return LunarHolidays.TryGetValue(d.ToString("yyyy-MM-dd"), out var lunar) ? lunar : null;
    }
}
