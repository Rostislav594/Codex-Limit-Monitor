using System.Diagnostics;
using System.ComponentModel;
using CodexLimitMonitor.Codex.Protocol;

namespace CodexLimitMonitor.Codex.AppServer;

public sealed class CodexAppServerConnection : ICodexAppServerConnection
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _stderrTask;
    private int _stderrLineCount;
    private bool _disposed;

    private CodexAppServerConnection(Process process, JsonlRpcClient client)
    {
        _process = process;
        Client = client;
        Completion = process.WaitForExitAsync();
        _stderrTask = ReadStderrAsync(_lifetime.Token);
    }

    public JsonlRpcClient Client { get; }

    public Task Completion { get; }

    public int StderrLineCount => Volatile.Read(ref _stderrLineCount);

    public static CodexAppServerConnection Start(TimeSpan? requestTimeout = null)
    {
        var process = new Process { StartInfo = CodexCommandLocator.CreateAppServerStartInfo() };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Codex App Server process did not start.");
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            process.Dispose();
            throw new CodexAppServerUnavailableException(exception);
        }

        var transport = new StreamJsonlTransport(process.StandardOutput, process.StandardInput);
        var client = new JsonlRpcClient(transport, requestTimeout);
        return new CodexAppServerConnection(process, client);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Client.DisposeAsync();

        try
        {
            using var shutdown = new CancellationTokenSource(ShutdownTimeout);
            await _process.WaitForExitAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _lifetime.Cancel();
            try
            {
                await _stderrTask;
            }
            catch (OperationCanceledException)
            {
            }

            _lifetime.Dispose();
            _process.Dispose();
        }
    }

    private async Task ReadStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync(cancellationToken) is not null)
            {
                Interlocked.Increment(ref _stderrLineCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
