using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.Core.RateLimits;

public static class RateLimitNormalizer
{
    public static RateLimitSnapshot Normalize(
        IEnumerable<RateLimitSourceSnapshot> sourceSnapshots,
        DateTimeOffset retrievedAt,
        ConnectionState state = ConnectionState.Connected,
        string? diagnosticMessage = null)
    {
        ArgumentNullException.ThrowIfNull(sourceSnapshots);

        var sources = sourceSnapshots.ToArray();
        var showLimitName = sources.Length > 1;
        var buckets = new List<RateLimitBucket>(sources.Length * 2);

        foreach (var source in sources)
        {
            AddBucket(buckets, source, source.Primary, RateLimitWindowKind.Primary, showLimitName);
            AddBucket(buckets, source, source.Secondary, RateLimitWindowKind.Secondary, showLimitName);
        }

        var planTypes = sources
            .Select(source => source.PlanType)
            .Where(planType => !string.IsNullOrWhiteSpace(planType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RateLimitSnapshot(
            Buckets: buckets.ToArray(),
            PlanType: planTypes.Length == 1 ? planTypes[0] : null,
            RateLimitReachedType: sources
                .Select(source => source.RateLimitReachedType)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            RetrievedAt: retrievedAt,
            State: buckets.Count == 0 && state == ConnectionState.Connected
                ? ConnectionState.NoRateLimitData
                : state,
            DiagnosticMessage: diagnosticMessage);
    }

    private static void AddBucket(
        ICollection<RateLimitBucket> buckets,
        RateLimitSourceSnapshot source,
        RateLimitSourceWindow? window,
        RateLimitWindowKind windowKind,
        bool showLimitName)
    {
        if (window?.UsedPercent is not double rawUsedPercent)
        {
            return;
        }

        var usedPercent = RateLimitMath.NormalizeUsedPercent(rawUsedPercent);
        var durationLabel = RateLimitLabelFormatter.FormatWindowDuration(window.WindowDurationMins);
        var displayName = showLimitName && !string.IsNullOrWhiteSpace(source.LimitName)
            ? $"{source.LimitName} • {durationLabel}"
            : durationLabel;
        var windowKey = windowKind.ToString().ToLowerInvariant();

        buckets.Add(new RateLimitBucket(
            Key: $"{source.SourceKey}:{windowKey}",
            LimitId: source.LimitId,
            LimitName: source.LimitName,
            DisplayName: displayName,
            WindowKind: windowKind,
            UsedPercent: usedPercent,
            RemainingPercent: RateLimitMath.CalculateRemainingPercent(rawUsedPercent),
            WindowDurationMins: window.WindowDurationMins,
            ResetsAt: RateLimitTime.FromUnixSeconds(window.ResetsAtUnixSeconds),
            PlanType: source.PlanType));
    }
}
