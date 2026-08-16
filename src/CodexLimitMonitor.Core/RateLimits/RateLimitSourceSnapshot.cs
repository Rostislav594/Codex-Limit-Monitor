namespace CodexLimitMonitor.Core.RateLimits;

public sealed record RateLimitSourceSnapshot(
    string SourceKey,
    string? LimitId,
    string? LimitName,
    string? PlanType,
    string? RateLimitReachedType,
    RateLimitSourceWindow? Primary,
    RateLimitSourceWindow? Secondary);

public sealed record RateLimitSourceWindow(
    double? UsedPercent,
    int? WindowDurationMins,
    long? ResetsAtUnixSeconds);
