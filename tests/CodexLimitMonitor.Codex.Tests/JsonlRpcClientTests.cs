using System.Text.Json;
using CodexLimitMonitor.Codex.Protocol;

namespace CodexLimitMonitor.Codex.Tests;

public sealed class JsonlRpcClientTests
{
    [Fact]
    public async Task CorrelatesOutOfOrderResponsesWhileDispatchingNotification()
    {
        var transport = new FakeJsonlTransport();
        await using var client = new JsonlRpcClient(transport);
        var notification = new TaskCompletionSource<RpcNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.NotificationReceived += value => notification.TrySetResult(value);

        var firstTask = client.SendRequestAsync<TestResponse>("first", parameters: null);
        var firstRequest = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement.Clone();
        var secondTask = client.SendRequestAsync<TestResponse>("second", new { value = 2 });
        var secondRequest = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement.Clone();

        await transport.SendServerLineAsync("{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{}}}");
        await transport.SendServerLineAsync($"{{\"id\":{secondRequest.GetProperty("id").GetInt64()},\"result\":{{\"value\":22}}}}");
        await transport.SendServerLineAsync($"{{\"id\":{firstRequest.GetProperty("id").GetInt64()},\"result\":{{\"value\":11}}}}");

        Assert.Equal(11, (await firstTask).Value);
        Assert.Equal(22, (await secondTask).Value);
        Assert.Equal("account/rateLimits/updated", (await notification.Task).Method);
    }

    [Fact]
    public async Task MalformedLine_DoesNotPreventFollowingResponse()
    {
        var transport = new FakeJsonlTransport();
        await using var client = new JsonlRpcClient(transport);

        var responseTask = client.SendRequestAsync<TestResponse>("test", parameters: null);
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement.Clone();
        await transport.SendServerLineAsync("not-json");
        await transport.SendServerLineAsync($"{{\"id\":{request.GetProperty("id").GetInt64()},\"result\":{{\"value\":7}}}}");

        Assert.Equal(7, (await responseTask).Value);
        Assert.Equal(1, client.MalformedLineCount);
    }

    [Fact]
    public async Task ThrowingNotificationSubscriber_DoesNotStopProtocolReader()
    {
        var transport = new FakeJsonlTransport();
        await using var client = new JsonlRpcClient(transport);
        client.NotificationReceived += _ => throw new InvalidOperationException("subscriber failed");

        var responseTask = client.SendRequestAsync<TestResponse>("test", parameters: null);
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement.Clone();
        await transport.SendServerLineAsync("{\"method\":\"account/rateLimits/updated\",\"params\":{}}");
        await transport.SendServerLineAsync($"{{\"id\":{request.GetProperty("id").GetInt64()},\"result\":{{\"value\":9}}}}");

        Assert.Equal(9, (await responseTask).Value);
    }

    [Fact]
    public async Task RpcError_SuppressesServerMessage()
    {
        var transport = new FakeJsonlTransport();
        await using var client = new JsonlRpcClient(transport);

        var responseTask = client.SendRequestAsync<TestResponse>("test", parameters: null);
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement.Clone();
        await transport.SendServerLineAsync(
            $"{{\"id\":{request.GetProperty("id").GetInt64()},\"error\":{{\"code\":401,\"message\":\"secret@example.com\"}}}}");

        var exception = await Assert.ThrowsAsync<RpcException>(() => responseTask);
        Assert.Equal(401, exception.Code);
        Assert.DoesNotContain("secret@example.com", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestWithoutResponse_TimesOut()
    {
        var transport = new FakeJsonlTransport();
        await using var client = new JsonlRpcClient(transport, TimeSpan.FromMilliseconds(50));

        var responseTask = client.SendRequestAsync<TestResponse>("test", parameters: null);
        _ = await transport.ReadClientLineAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => responseTask);
    }

    private sealed record TestResponse(int Value);
}
