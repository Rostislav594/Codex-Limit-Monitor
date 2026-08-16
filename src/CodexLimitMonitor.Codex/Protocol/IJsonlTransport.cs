namespace CodexLimitMonitor.Codex.Protocol;

public interface IJsonlTransport : IAsyncDisposable
{
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}
