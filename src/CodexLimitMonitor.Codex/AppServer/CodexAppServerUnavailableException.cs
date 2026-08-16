namespace CodexLimitMonitor.Codex.AppServer;

internal sealed class CodexAppServerUnavailableException(Exception innerException)
    : Exception("Codex App Server is unavailable.", innerException);
