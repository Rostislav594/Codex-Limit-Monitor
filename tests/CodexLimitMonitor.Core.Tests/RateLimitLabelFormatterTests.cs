using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Core.Tests;

public sealed class RateLimitLabelFormatterTests
{
    [Theory]
    [InlineData(300, "5 часов")]
    [InlineData(10_080, "Неделя")]
    [InlineData(1_440, "Лимит • 1440 мин")]
    [InlineData(null, "Лимит")]
    public void FormatWindowDuration_ReturnsFriendlyOrNeutralLabel(int? minutes, string expected)
    {
        Assert.Equal(expected, RateLimitLabelFormatter.FormatWindowDuration(minutes));
    }
}
