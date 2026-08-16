using System.Text.Json;
using CodexLimitMonitor.Codex.Dto;
using CodexLimitMonitor.Codex.RateLimits;

namespace CodexLimitMonitor.Codex.Tests;

public sealed class RateLimitDtoTests
{
    [Fact]
    public void PartialDto_DeserializesWithoutThrowing()
    {
        const string json = """
            {
              "rateLimits": {
                "primary": {},
                "unknownFutureField": { "enabled": true }
              }
            }
            """;

        var response = JsonSerializer.Deserialize(
            json,
            RateLimitDtoJsonContext.Default.GetAccountRateLimitsResponseDto);

        Assert.NotNull(response);
        Assert.NotNull(response.RateLimits?.Primary);
        Assert.Null(response.RateLimits.Primary.UsedPercent);
    }

    [Fact]
    public void Mapper_PrefersNamedLimitMapOverLegacySnapshot()
    {
        var response = new GetAccountRateLimitsResponseDto
        {
            RateLimits = Snapshot("legacy", usedPercent: 1),
            RateLimitsByLimitId = new Dictionary<string, RateLimitSnapshotDto?>
            {
                ["codex"] = Snapshot(limitId: null, usedPercent: 20),
            },
        };

        var source = Assert.Single(RateLimitResponseMapper.ToSourceSnapshots(response));

        Assert.Equal("codex", source.SourceKey);
        Assert.Equal("codex", source.LimitId);
        Assert.Equal(20, source.Primary?.UsedPercent);
    }

    [Fact]
    public void Mapper_NullNamedEntry_FallsBackToLegacySnapshot()
    {
        var response = new GetAccountRateLimitsResponseDto
        {
            RateLimits = Snapshot("legacy", usedPercent: 15),
            RateLimitsByLimitId = new Dictionary<string, RateLimitSnapshotDto?>
            {
                ["malformed"] = null,
            },
        };

        var source = Assert.Single(RateLimitResponseMapper.ToSourceSnapshots(response));

        Assert.Equal("legacy", source.SourceKey);
        Assert.Equal(15, source.Primary?.UsedPercent);
    }

    private static RateLimitSnapshotDto Snapshot(string? limitId, int usedPercent) => new()
    {
        LimitId = limitId,
        Primary = new RateLimitWindowDto
        {
            UsedPercent = usedPercent,
            WindowDurationMins = 300,
        },
    };
}
