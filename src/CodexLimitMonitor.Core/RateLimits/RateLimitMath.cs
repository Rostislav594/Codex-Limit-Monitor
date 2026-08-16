namespace CodexLimitMonitor.Core.RateLimits;

public static class RateLimitMath
{
    public static double NormalizeUsedPercent(double usedPercent)
    {
        if (double.IsNaN(usedPercent))
        {
            return 100;
        }

        return Math.Clamp(usedPercent, 0, 100);
    }

    public static double CalculateRemainingPercent(double usedPercent) =>
        100 - NormalizeUsedPercent(usedPercent);
}
