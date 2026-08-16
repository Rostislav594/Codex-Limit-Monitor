namespace CodexLimitMonitor.Core.Models;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    CodexNotFound,
    AppServerUnavailable,
    NotSignedIn,
    RateLimitsUnavailable,
    Offline,
    ServerError,
    NoRateLimitData,
}
