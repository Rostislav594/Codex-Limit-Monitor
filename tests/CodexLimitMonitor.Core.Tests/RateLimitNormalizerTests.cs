using CodexLimitMonitor.Core.Models;
using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Core.Tests;

public sealed class RateLimitNormalizerTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_PrimaryOnly_CreatesOneBucket()
    {
        var source = CreateSource(
            primary: new RateLimitSourceWindow(25, 300, 1_900_000_000),
            secondary: null);

        var result = RateLimitNormalizer.Normalize([source], RetrievedAt);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal("codex:primary", bucket.Key);
        Assert.Equal("5 часов", bucket.DisplayName);
        Assert.Equal(25, bucket.UsedPercent);
        Assert.Equal(75, bucket.RemainingPercent);
        Assert.False(bucket.IsExhausted);
        Assert.Equal(ConnectionState.Connected, result.State);
    }

    [Fact]
    public void Normalize_SecondaryOnly_DoesNotRequirePrimary()
    {
        var source = CreateSource(
            primary: null,
            secondary: new RateLimitSourceWindow(40, 10_080, null));

        var result = RateLimitNormalizer.Normalize([source], RetrievedAt);

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(RateLimitWindowKind.Secondary, bucket.WindowKind);
        Assert.Equal("Неделя", bucket.DisplayName);
    }

    [Fact]
    public void Normalize_UnknownDuration_IsNotDiscarded()
    {
        var source = CreateSource(
            primary: new RateLimitSourceWindow(10, 1_440, null),
            secondary: null);

        var bucket = Assert.Single(RateLimitNormalizer.Normalize([source], RetrievedAt).Buckets);

        Assert.Equal("Лимит • 1440 мин", bucket.DisplayName);
    }

    [Fact]
    public void Normalize_MultipleNamedLimits_ProducesStableDistinctKeysAndLabels()
    {
        var codex = CreateSource(
            primary: new RateLimitSourceWindow(10, 300, null),
            secondary: null);
        var review = CreateSource(
            sourceKey: "review",
            limitId: "review",
            limitName: "Code Review",
            primary: new RateLimitSourceWindow(20, 300, null),
            secondary: null);

        var result = RateLimitNormalizer.Normalize([codex, review], RetrievedAt);

        Assert.Collection(
            result.Buckets,
            bucket =>
            {
                Assert.Equal("codex:primary", bucket.Key);
                Assert.Equal("Codex • 5 часов", bucket.DisplayName);
            },
            bucket =>
            {
                Assert.Equal("review:primary", bucket.Key);
                Assert.Equal("Code Review • 5 часов", bucket.DisplayName);
            });
    }

    [Fact]
    public void Normalize_WindowWithoutUsedPercent_IsIgnoredAndProducesNoDataState()
    {
        var source = CreateSource(
            primary: new RateLimitSourceWindow(null, 300, null),
            secondary: null);

        var result = RateLimitNormalizer.Normalize([source], RetrievedAt);

        Assert.Empty(result.Buckets);
        Assert.Equal(ConnectionState.NoRateLimitData, result.State);
    }

    private static RateLimitSourceSnapshot CreateSource(
        RateLimitSourceWindow? primary,
        RateLimitSourceWindow? secondary,
        string sourceKey = "codex",
        string? limitId = "codex",
        string? limitName = "Codex") =>
        new(
            SourceKey: sourceKey,
            LimitId: limitId,
            LimitName: limitName,
            PlanType: "pro",
            RateLimitReachedType: null,
            Primary: primary,
            Secondary: secondary);
}
