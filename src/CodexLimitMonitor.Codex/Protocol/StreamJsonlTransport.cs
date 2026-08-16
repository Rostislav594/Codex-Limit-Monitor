namespace CodexLimitMonitor.Codex.Protocol;

public sealed class StreamJsonlTransport(
    TextReader reader,
    TextWriter writer,
    bool leaveOpen = false) : IJsonlTransport
{
    private bool _disposed;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await reader.ReadLineAsync(cancellationToken);
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (!leaveOpen)
        {
            writer.Dispose();
            reader.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
