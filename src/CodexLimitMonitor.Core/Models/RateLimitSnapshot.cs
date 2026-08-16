namespace CodexLimitMonitor.Core.Models;

public sealed record RateLimitSnapshot(
    IReadOnlyList<RateLimitBucket> Buckets,
    string? PlanType,
    string? RateLimitReachedType,
    DateTimeOffset RetrievedAt,
    ConnectionState State,
    string? DiagnosticMessage)
{
    public bool HasData => Buckets.Count > 0;

    public bool IsStale => State is not ConnectionState.Connected;
}
