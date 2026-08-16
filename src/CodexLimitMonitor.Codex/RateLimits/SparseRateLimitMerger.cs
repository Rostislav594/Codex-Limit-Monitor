using CodexLimitMonitor.Codex.Dto;

namespace CodexLimitMonitor.Codex.RateLimits;

public static class SparseRateLimitMerger
{
    public static GetAccountRateLimitsResponseDto ApplyUpdate(
        GetAccountRateLimitsResponseDto baseline,
        AccountRateLimitsUpdatedNotificationDto update)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(update);

        if (update.RateLimits is null)
        {
            return baseline;
        }

        var mergedLegacy = Merge(baseline.RateLimits, update.RateLimits);
        var mergedNamedLimits = MergeNamedLimits(baseline.RateLimitsByLimitId, update.RateLimits);

        return baseline with
        {
            RateLimits = mergedLegacy,
            RateLimitsByLimitId = mergedNamedLimits,
        };
    }

    public static RateLimitSnapshotDto Merge(
        RateLimitSnapshotDto? baseline,
        RateLimitSnapshotDto update)
    {
        ArgumentNullException.ThrowIfNull(update);
        baseline ??= new RateLimitSnapshotDto();

        return baseline with
        {
            LimitId = update.LimitId ?? baseline.LimitId,
            LimitName = update.LimitName ?? baseline.LimitName,
            PlanType = update.PlanType ?? baseline.PlanType,
            Primary = MergeWindow(baseline.Primary, update.Primary),
            Secondary = MergeWindow(baseline.Secondary, update.Secondary),
            RateLimitReachedType = update.RateLimitReachedType ?? baseline.RateLimitReachedType,
            SpendControlReached = update.SpendControlReached ?? baseline.SpendControlReached,
        };
    }

    private static Dictionary<string, RateLimitSnapshotDto?>? MergeNamedLimits(
        Dictionary<string, RateLimitSnapshotDto?>? baseline,
        RateLimitSnapshotDto update)
    {
        if (baseline is null)
        {
            return null;
        }

        var result = new Dictionary<string, RateLimitSnapshotDto?>(baseline, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(update.LimitId))
        {
            return result;
        }

        var key = result
            .FirstOrDefault(pair =>
                string.Equals(pair.Key, update.LimitId, StringComparison.Ordinal) ||
                string.Equals(pair.Value?.LimitId, update.LimitId, StringComparison.Ordinal))
            .Key;

        if (key is not null)
        {
            result[key] = Merge(result[key], update);
        }
        else
        {
            result[update.LimitId] = update;
        }

        return result;
    }

    private static RateLimitWindowDto? MergeWindow(
        RateLimitWindowDto? baseline,
        RateLimitWindowDto? update)
    {
        if (update is null)
        {
            return baseline;
        }

        baseline ??= new RateLimitWindowDto();
        return baseline with
        {
            UsedPercent = update.UsedPercent ?? baseline.UsedPercent,
            WindowDurationMins = update.WindowDurationMins ?? baseline.WindowDurationMins,
            ResetsAt = update.ResetsAt ?? baseline.ResetsAt,
        };
    }
}
