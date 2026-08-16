using System.Threading.Channels;
using CodexLimitMonitor.Codex.Protocol;

namespace CodexLimitMonitor.Codex.Tests;

internal sealed class FakeJsonlTransport : IJsonlTransport
{
    private readonly Channel<string> _clientWrites = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _serverWrites = Channel.CreateUnbounded<string>();

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        ReadServerLineAsync(cancellationToken);

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
        _clientWrites.Writer.WriteAsync(line, cancellationToken);

    public ValueTask<string> ReadClientLineAsync(CancellationToken cancellationToken = default) =>
        _clientWrites.Reader.ReadAsync(cancellationToken);

    public ValueTask SendServerLineAsync(string line, CancellationToken cancellationToken = default) =>
        _serverWrites.Writer.WriteAsync(line, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _clientWrites.Writer.TryComplete();
        _serverWrites.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<string?> ReadServerLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _serverWrites.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }
}
