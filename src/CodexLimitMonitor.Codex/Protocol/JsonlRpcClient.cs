using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexLimitMonitor.Codex.Protocol;

public sealed class JsonlRpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IJsonlTransport _transport;
    private readonly TimeSpan _requestTimeout;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Task _readerTask;
    private long _nextRequestId;
    private int _malformedLineCount;
    private bool _disposed;

    public JsonlRpcClient(IJsonlTransport transport, TimeSpan? requestTimeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        _readerTask = ReadMessagesAsync(_lifetime.Token);
    }

    public event Action<RpcNotification>? NotificationReceived;

    public int MalformedLineCount => Volatile.Read(ref _malformedLineCount);

    public async Task<TResponse> SendRequestAsync<TResponse>(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Could not register the RPC request.");
        }

        try
        {
            await WriteMessageAsync(CreateRequest(method, requestId, parameters), cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(_requestTimeout);
            var result = await completion.Task.WaitAsync(timeout.Token);

            if (typeof(TResponse) == typeof(JsonElement))
            {
                return (TResponse)(object)result;
            }

            return result.Deserialize<TResponse>(SerializerOptions)
                ?? throw new JsonException($"RPC result for '{method}' was null or incompatible.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"RPC request '{method}' timed out after {_requestTimeout.TotalSeconds:0.###} seconds.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public Task SendNotificationAsync(string method, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return WriteMessageAsync(new Dictionary<string, object?> { ["method"] = method }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        FailPendingRequests(new IOException("JSONL RPC client stopped."));
        await _transport.DisposeAsync();

        try
        {
            await _readerTask;
        }
        catch (OperationCanceledException)
        {
        }

        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private static Dictionary<string, object?> CreateRequest(string method, long id, object? parameters)
    {
        var request = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["id"] = id,
        };

        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        return request;
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, SerializerOptions);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _transport.WriteLineAsync(json, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _transport.ReadLineAsync(cancellationToken) is { } line)
            {
                ProcessLine(line);
            }

            if (!_disposed)
            {
                FailPendingRequests(new IOException("Codex App Server closed its JSONL output."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            FailPendingRequests(new IOException("Codex App Server JSONL reader failed.", exception));
        }
    }

    private void ProcessLine(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            Interlocked.Increment(ref _malformedLineCount);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (TryGetRequestId(root, out var requestId) && _pending.TryGetValue(requestId, out var completion))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    completion.TrySetException(CreateRpcException(error));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(new RpcException(code: null));
                }

                return;
            }

            if (root.TryGetProperty("method", out var methodElement) &&
                methodElement.ValueKind == JsonValueKind.String &&
                methodElement.GetString() is { Length: > 0 } method)
            {
                JsonElement? parameters = root.TryGetProperty("params", out var paramsElement)
                    ? paramsElement.Clone()
                    : null;
                DispatchNotification(new RpcNotification(method, parameters));
            }
        }
    }

    private void DispatchNotification(RpcNotification notification)
    {
        if (NotificationReceived is not { } handlers)
        {
            return;
        }

        foreach (Action<RpcNotification> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(notification);
            }
            catch
            {
                // A UI or service subscriber must not terminate the protocol reader.
            }
        }
    }

    private static bool TryGetRequestId(JsonElement root, out long requestId)
    {
        requestId = default;
        return root.TryGetProperty("id", out var idElement) &&
               idElement.ValueKind == JsonValueKind.Number &&
               idElement.TryGetInt64(out requestId);
    }

    private static RpcException CreateRpcException(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
            ? parsedCode
            : (int?)null;
        return new RpcException(code);
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var pendingRequest in _pending.Values)
        {
            pendingRequest.TrySetException(exception);
        }
    }
}
