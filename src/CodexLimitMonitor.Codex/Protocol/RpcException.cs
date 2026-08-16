namespace CodexLimitMonitor.Codex.Protocol;

public sealed class RpcException(int? code)
    : Exception(code is null
        ? "Codex App Server returned an RPC error; server message suppressed."
        : $"Codex App Server returned RPC error {code}; server message suppressed.")
{
    public int? Code { get; } = code;
}
