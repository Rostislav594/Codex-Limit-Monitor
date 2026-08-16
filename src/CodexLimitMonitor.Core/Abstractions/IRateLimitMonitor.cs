using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.Core.Abstractions;

public interface IRateLimitMonitor : IAsyncDisposable
{
    event EventHandler<RateLimitSnapshot>? SnapshotChanged;

    event EventHandler<RateLimitMonitorDiagnostics>? DiagnosticsChanged;

    RateLimitSnapshot CurrentSnapshot { get; }

    RateLimitMonitorDiagnostics Diagnostics { get; }

    TimeSpan RefreshInterval { get; set; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    void RequestReconnect();
}
