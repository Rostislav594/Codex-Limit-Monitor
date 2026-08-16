namespace CodexLimitMonitor.Core.RateLimits;

public static class RateLimitLabelFormatter
{
    public static string FormatWindowDuration(int? windowDurationMins) => windowDurationMins switch
    {
        300 => "5 часов",
        10_080 => "Неделя",
        > 0 => $"Лимит • {windowDurationMins} мин",
        _ => "Лимит",
    };
}
