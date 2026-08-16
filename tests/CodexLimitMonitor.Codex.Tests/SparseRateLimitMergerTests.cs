using CodexLimitMonitor.Codex.Dto;
using CodexLimitMonitor.Codex.RateLimits;

namespace CodexLimitMonitor.Codex.Tests;

public sealed class SparseRateLimitMergerTests
{
    [Fact]
    public void ApplyUpdate_PreservesMissingWindowMetadataAndSecondaryWindow()
    {
        var baselineSnapshot = new RateLimitSnapshotDto
        {
            LimitId = "codex",
            LimitName = "Codex",
            PlanType = "pro",
            Primary = new RateLimitWindowDto
            {
                UsedPercent = 10,
                WindowDurationMins = 300,
                ResetsAt = 1_900_000_000,
            },
            Secondary = new RateLimitWindowDto
            {
                UsedPercent = 20,
                WindowDurationMins = 10_080,
                ResetsAt = 1_900_100_000,
            },
        };
        var baseline = new GetAccountRateLimitsResponseDto
        {
            RateLimits = baselineSnapshot,
            RateLimitsByLimitId = new Dictionary<string, RateLimitSnapshotDto?>
            {
                ["codex"] = baselineSnapshot,
            },
        };
        var update = new AccountRateLimitsUpdatedNotificationDto
        {
            RateLimits = new RateLimitSnapshotDto
            {
                LimitId = "codex",
                Primary = new RateLimitWindowDto { UsedPercent = 40 },
            },
        };

        var result = SparseRateLimitMerger.ApplyUpdate(baseline, update);

        Assert.Equal(40, result.RateLimits?.Primary?.UsedPercent);
        Assert.Equal(300, result.RateLimits?.Primary?.WindowDurationMins);
        Assert.Equal(1_900_000_000, result.RateLimits?.Primary?.ResetsAt);
        Assert.Equal(20, result.RateLimits?.Secondary?.UsedPercent);
        Assert.Equal("pro", result.RateLimits?.PlanType);
        Assert.Equal(40, result.RateLimitsByLimitId?["codex"]?.Primary?.UsedPercent);
    }

    [Fact]
    public void ApplyUpdate_NullSnapshotLeavesBaselineUnchanged()
    {
        var baseline = new GetAccountRateLimitsResponseDto
        {
            RateLimits = new RateLimitSnapshotDto { LimitId = "codex" },
        };

        var result = SparseRateLimitMerger.ApplyUpdate(
            baseline,
            new AccountRateLimitsUpdatedNotificationDto());

        Assert.Same(baseline, result);
    }
}
