namespace CodexLimitMonitor.Codex.AppServer;

internal sealed class CodexDiscoveryException(string userMessage) : FileNotFoundException(userMessage)
{
    public string UserMessage { get; } = userMessage;
}
