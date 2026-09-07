namespace PIHarness.App.Presentation;

public static class RelativeActivityTime
{
    public static string Format(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var minutes = Math.Max(0, (long)(now - timestamp).TotalMinutes);
        if (minutes == 0) return "不到 1 分钟";
        if (minutes < 60) return $"{minutes} 分钟前";
        if (minutes < 1440) return $"{minutes / 60} 小时前";
        return $"{minutes / 1440} 天前";
    }
}
