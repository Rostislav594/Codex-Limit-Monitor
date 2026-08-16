using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Core.Tests;

public sealed class RateLimitTimeTests
{
    [Fact]
    public void FromUnixSeconds_ConvertsValidTimestamp()
    {
        var result = RateLimitTime.FromUnixSeconds(1_730_947_200);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_730_947_200), result);
    }

    [Fact]
    public void FromUnixSeconds_ReturnsNullForOutOfRangeTimestamp()
    {
        Assert.Null(RateLimitTime.FromUnixSeconds(long.MaxValue));
    }

    [Fact]
    public void GetTimeUntilReset_ClampsPastResetToZero()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, RateLimitTime.GetTimeUntilReset(now.AddMinutes(-1), now));
        Assert.Equal(TimeSpan.FromMinutes(5), RateLimitTime.GetTimeUntilReset(now.AddMinutes(5), now));
        Assert.Null(RateLimitTime.GetTimeUntilReset(null, now));
    }
}
