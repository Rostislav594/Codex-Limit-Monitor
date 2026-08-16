using System.IO.Pipes;

namespace CodexLimitMonitor.App.Services;

internal sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private const string MutexName = @"Local\CodexLimitMonitor.SingleInstance";
    private const string PipeName = "CodexLimitMonitor.Activation";

    private readonly CancellationTokenSource _lifetime = new();
    private readonly Mutex _mutex;
    private Task? _listenerTask;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public event EventHandler? ActivationRequested;

    public event EventHandler? ShutdownRequested;

    public bool IsPrimaryInstance { get; }

    public void StartListening()
    {
        if (!IsPrimaryInstance || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = ListenAsync(_lifetime.Token);
    }

    public async Task SignalPrimaryAsync(
        SingleInstanceCommand command = SingleInstanceCommand.Activate,
        CancellationToken cancellationToken = default)
    {
        if (IsPrimaryInstance)
        {
            return;
        }

        try
        {
            await using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);
            await client.ConnectAsync(timeout: 1500, cancellationToken);
            await client.WriteAsync(new[] { (byte)command }, cancellationToken);
            await client.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _lifetime.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(cancellationToken);

            var buffer = new byte[1];
            if (await server.ReadAsync(buffer, cancellationToken) > 0)
            {
                if ((SingleInstanceCommand)buffer[0] == SingleInstanceCommand.ShutdownForUpdate)
                {
                    ShutdownRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}

internal enum SingleInstanceCommand : byte
{
    Activate = 1,
    ShutdownForUpdate = 2,
}
