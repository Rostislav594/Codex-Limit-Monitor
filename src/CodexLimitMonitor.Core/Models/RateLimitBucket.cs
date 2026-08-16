using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Core.Models;

public sealed record RateLimitBucket(
    string Key,
    string? LimitId,
    string? LimitName,
    string DisplayName,
    RateLimitWindowKind WindowKind,
    double UsedPercent,
    double RemainingPercent,
    int? WindowDurationMins,
    DateTimeOffset? ResetsAt,
    string? PlanType)
{
    public bool IsExhausted => RemainingPercent <= 0;

    public TimeSpan? GetTimeUntilReset(DateTimeOffset now) =>
        RateLimitTime.GetTimeUntilReset(ResetsAt, now);
}
