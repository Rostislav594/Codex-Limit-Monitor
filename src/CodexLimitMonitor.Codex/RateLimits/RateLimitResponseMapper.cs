using CodexLimitMonitor.Codex.Dto;
using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Codex.RateLimits;

public static class RateLimitResponseMapper
{
    public static IReadOnlyList<RateLimitSourceSnapshot> ToSourceSnapshots(
        GetAccountRateLimitsResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.RateLimitsByLimitId is { Count: > 0 } namedLimits)
        {
            var mappedNamedLimits = namedLimits
                .Where(pair => pair.Value is not null)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Map(pair.Key, pair.Value!))
                .ToArray();

            if (mappedNamedLimits.Length > 0)
            {
                return mappedNamedLimits;
            }
        }

        return response.RateLimits is null
            ? []
            : [Map(response.RateLimits.LimitId ?? "legacy", response.RateLimits)];
    }

    private static RateLimitSourceSnapshot Map(string sourceKey, RateLimitSnapshotDto snapshot) =>
        new(
            SourceKey: sourceKey,
            LimitId: snapshot.LimitId ?? sourceKey,
            LimitName: snapshot.LimitName,
            PlanType: snapshot.PlanType,
            RateLimitReachedType: snapshot.RateLimitReachedType,
            Primary: Map(snapshot.Primary),
            Secondary: Map(snapshot.Secondary));

    private static RateLimitSourceWindow? Map(RateLimitWindowDto? window) => window is null
        ? null
        : new RateLimitSourceWindow(window.UsedPercent, window.WindowDurationMins, window.ResetsAt);
}
