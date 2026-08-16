using System.Text.Json;
using CodexLimitMonitor.Codex.AppServer;

namespace CodexLimitMonitor.ProtocolProbe;

internal static class Program
{
    public static async Task<int> Main()
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            Console.Error.WriteLine("Locating Codex and starting App Server...");

            await using var connection = CodexAppServerConnection.Start();
            var probe = new AppServerProtocolProbe(connection.Client, () => connection.StderrLineCount);
            var summary = await probe.RunAsync(shutdown.Token);

            Console.WriteLine(JsonSerializer.Serialize(summary, ProbeJsonContext.Default.ProbeSummary));
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Console.Error.WriteLine("Protocol probe cancelled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Protocol probe failed: {exception.Message}");
            return 1;
        }
    }
}
