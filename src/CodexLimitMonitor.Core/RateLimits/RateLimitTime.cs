namespace CodexLimitMonitor.Core.RateLimits;

public static class RateLimitTime
{
    public static DateTimeOffset? FromUnixSeconds(long? unixSeconds)
    {
        if (unixSeconds is null)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static TimeSpan? GetTimeUntilReset(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
        {
            return null;
        }

        var remaining = resetsAt.Value - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
