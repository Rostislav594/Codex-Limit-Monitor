using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Core.Tests;

public sealed class RateLimitMathTests
{
    [Theory]
    [InlineData(-10, 100)]
    [InlineData(0, 100)]
    [InlineData(25, 75)]
    [InlineData(100, 0)]
    [InlineData(140, 0)]
    public void CalculateRemainingPercent_ClampsToValidRange(double used, double expectedRemaining)
    {
        Assert.Equal(expectedRemaining, RateLimitMath.CalculateRemainingPercent(used));
    }

    [Fact]
    public void CalculateRemainingPercent_TreatsNaNAsExhausted()
    {
        Assert.Equal(0, RateLimitMath.CalculateRemainingPercent(double.NaN));
    }
}
