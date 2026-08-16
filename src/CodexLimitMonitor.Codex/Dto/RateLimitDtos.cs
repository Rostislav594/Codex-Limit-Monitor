using System.Text.Json.Serialization;

namespace CodexLimitMonitor.Codex.Dto;

public sealed record GetAccountRateLimitsResponseDto
{
    [JsonPropertyName("rateLimits")]
    public RateLimitSnapshotDto? RateLimits { get; init; }

    [JsonPropertyName("rateLimitsByLimitId")]
    public Dictionary<string, RateLimitSnapshotDto?>? RateLimitsByLimitId { get; init; }
}

public sealed record AccountRateLimitsUpdatedNotificationDto
{
    [JsonPropertyName("rateLimits")]
    public RateLimitSnapshotDto? RateLimits { get; init; }
}

public sealed record RateLimitSnapshotDto
{
    [JsonPropertyName("limitId")]
    public string? LimitId { get; init; }

    [JsonPropertyName("limitName")]
    public string? LimitName { get; init; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; init; }

    [JsonPropertyName("primary")]
    public RateLimitWindowDto? Primary { get; init; }

    [JsonPropertyName("secondary")]
    public RateLimitWindowDto? Secondary { get; init; }

    [JsonPropertyName("rateLimitReachedType")]
    public string? RateLimitReachedType { get; init; }

    [JsonPropertyName("spendControlReached")]
    public bool? SpendControlReached { get; init; }
}

public sealed record RateLimitWindowDto
{
    [JsonPropertyName("usedPercent")]
    public int? UsedPercent { get; init; }

    [JsonPropertyName("windowDurationMins")]
    public int? WindowDurationMins { get; init; }

    [JsonPropertyName("resetsAt")]
    public long? ResetsAt { get; init; }
}

[JsonSerializable(typeof(GetAccountRateLimitsResponseDto))]
[JsonSerializable(typeof(AccountRateLimitsUpdatedNotificationDto))]
public partial class RateLimitDtoJsonContext : JsonSerializerContext;
