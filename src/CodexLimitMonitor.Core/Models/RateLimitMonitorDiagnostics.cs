namespace CodexLimitMonitor.Core.Models;

public sealed record RateLimitMonitorDiagnostics(
    int ConnectionAttempts,
    int ReconnectCount,
    int MalformedLineCount,
    int StderrLineCount,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSuccessfulRefresh)
{
    public static RateLimitMonitorDiagnostics Empty { get; } = new(0, 0, 0, 0, null, null);
}
