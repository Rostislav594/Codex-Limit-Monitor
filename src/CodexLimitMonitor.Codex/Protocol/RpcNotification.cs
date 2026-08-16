using System.Text.Json;

namespace CodexLimitMonitor.Codex.Protocol;

public sealed record RpcNotification(string Method, JsonElement? Parameters);
