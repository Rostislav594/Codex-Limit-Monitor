using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using CodexLimitMonitor.Codex.AppServer;
using CodexLimitMonitor.Codex.Protocol;
using CodexLimitMonitor.Codex.RateLimits;
using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.Codex.Tests;

public sealed class CodexRateLimitMonitorReconnectTests
{
    [Fact]
    public async Task UnavailableAppServerPublishesDistinctState()
    {
        await using var monitor = new CodexRateLimitMonitor(
            () => throw new CodexAppServerUnavailableException(new InvalidOperationException()),
            reconnectBackoff: [TimeSpan.FromSeconds(30)]);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.AppServerUnavailable);

        Assert.Contains("App Server", monitor.CurrentSnapshot.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableInitialRateLimitsPublishDistinctState()
    {
        var connection = new FakeConnection(usedPercent: 0, rateLimitsAvailable: false);
        await using var monitor = new CodexRateLimitMonitor(
            () => connection,
            reconnectBackoff: [TimeSpan.FromSeconds(30)]);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.RateLimitsUnavailable);

        Assert.Contains("лимиты временно недоступны", monitor.CurrentSnapshot.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCodexPublishesActionableState()
    {
        await using var monitor = new CodexRateLimitMonitor(
            () => throw new FileNotFoundException("codex"),
            reconnectBackoff: [TimeSpan.Zero]);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.CodexNotFound);

        Assert.Contains("PATH", monitor.CurrentSnapshot.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedOutAccountPublishesSignInState()
    {
        var connection = new FakeConnection(usedPercent: 0, signedIn: false);
        await using var monitor = new CodexRateLimitMonitor(
            () => connection,
            reconnectBackoff: [TimeSpan.Zero]);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.NotSignedIn);

        Assert.False(monitor.CurrentSnapshot.HasData);
    }

    [Fact]
    public async Task ProcessExitPreservesStaleDataAndReconnects()
    {
        var first = new FakeConnection(usedPercent: 20);
        var second = new FakeConnection(usedPercent: 35);
        var connections = new ConcurrentQueue<ICodexAppServerConnection>([first, second]);
        await using var monitor = new CodexRateLimitMonitor(
            () => connections.TryDequeue(out var connection)
                ? connection
                : throw new InvalidOperationException("No fake connection available."),
            refreshInterval: TimeSpan.FromHours(1),
            reconnectBackoff: [TimeSpan.FromMilliseconds(10)]);
        var snapshots = new ConcurrentQueue<RateLimitSnapshot>();
        monitor.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.Connected);
        Assert.Equal(80, Assert.Single(monitor.CurrentSnapshot.Buckets).RemainingPercent);

        first.Exit();

        await WaitUntilAsync(() => snapshots.Any(snapshot =>
            snapshot.State == ConnectionState.Reconnecting && snapshot.HasData));
        await WaitUntilAsync(() =>
            monitor.CurrentSnapshot.State == ConnectionState.Connected &&
            Assert.Single(monitor.CurrentSnapshot.Buckets).RemainingPercent == 65);

        Assert.True(monitor.Diagnostics.ConnectionAttempts >= 2);
        Assert.True(monitor.Diagnostics.ReconnectCount >= 1);
    }

    [Fact]
    public async Task ExplicitReconnectStartsFreshSession()
    {
        var first = new FakeConnection(usedPercent: 10);
        var second = new FakeConnection(usedPercent: 15);
        var connections = new ConcurrentQueue<ICodexAppServerConnection>([first, second]);
        await using var monitor = new CodexRateLimitMonitor(
            () => connections.TryDequeue(out var connection)
                ? connection
                : throw new InvalidOperationException("No fake connection available."),
            refreshInterval: TimeSpan.FromHours(1),
            reconnectBackoff: [TimeSpan.Zero]);

        await monitor.StartAsync();
        await WaitUntilAsync(() => monitor.CurrentSnapshot.State == ConnectionState.Connected);
        monitor.RequestReconnect();
        await WaitUntilAsync(() =>
            monitor.Diagnostics.ConnectionAttempts >= 2 &&
            monitor.CurrentSnapshot.State == ConnectionState.Connected);

        Assert.Equal(85, Assert.Single(monitor.CurrentSnapshot.Buckets).RemainingPercent);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeConnection : ICodexAppServerConnection
    {
        private readonly FakeJsonlTransport _transport = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;

        public FakeConnection(
            int usedPercent,
            bool signedIn = true,
            bool rateLimitsAvailable = true)
        {
            Client = new JsonlRpcClient(_transport, TimeSpan.FromSeconds(1));
            _serverTask = RespondAsync(usedPercent, signedIn, rateLimitsAvailable);
        }

        public JsonlRpcClient Client { get; }

        public Task Completion => _completion.Task;

        public int StderrLineCount => 0;

        public void Exit() => _completion.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _serverTask;
            _completion.TrySetResult();
        }

        private async Task RespondAsync(
            int usedPercent,
            bool signedIn,
            bool rateLimitsAvailable)
        {
            try
            {
                while (true)
                {
                    var line = await _transport.ReadClientLineAsync();
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var id))
                    {
                        continue;
                    }

                    var method = root.GetProperty("method").GetString();
                    if (method == "account/rateLimits/read" && !rateLimitsAvailable)
                    {
                        await _transport.SendServerLineAsync(JsonSerializer.Serialize(new
                        {
                            id = id.GetInt64(),
                            error = new { code = -32000, message = "Synthetic test failure" },
                        }));
                        continue;
                    }

                    var result = method switch
                    {
                        "account/read" => signedIn
                            ? "{\"account\":{\"type\":\"chatgpt\"}}"
                            : "{\"account\":null}",
                        "account/rateLimits/read" =>
                            $"{{\"rateLimits\":{{\"limitId\":\"codex\",\"planType\":\"plus\",\"primary\":{{\"usedPercent\":{usedPercent},\"windowDurationMins\":300}}}}}}",
                        _ => "{}",
                    };
                    var response = JsonSerializer.Serialize(new
                    {
                        id = id.GetInt64(),
                        result = JsonSerializer.Deserialize<JsonElement>(result),
                    });
                    await _transport.SendServerLineAsync(response);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ChannelClosedException)
            {
            }
        }
    }
}
