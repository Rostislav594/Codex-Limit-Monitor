using System.Text.Json;
using System.Threading.Channels;
using CodexLimitMonitor.Codex.AppServer;
using CodexLimitMonitor.Codex.Dto;
using CodexLimitMonitor.Codex.Protocol;
using CodexLimitMonitor.Core.Abstractions;
using CodexLimitMonitor.Core.Models;
using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.Codex.RateLimits;

public sealed class CodexRateLimitMonitor : IRateLimitMonitor
{
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly IReadOnlyList<TimeSpan> DefaultReconnectBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private readonly Func<ICodexAppServerConnection> _connectionFactory;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<TimeSpan> _reconnectBackoff;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly Channel<bool> _reconnectSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    private ICodexAppServerConnection? _connection;
    private GetAccountRateLimitsResponseDto? _baseline;
    private Task? _supervisorTask;
    private long _refreshIntervalTicks;
    private int _connectionAttempts;
    private int _reconnectCount;
    private bool _started;
    private bool _disposed;

    public CodexRateLimitMonitor(
        TimeSpan? refreshInterval = null,
        TimeProvider? timeProvider = null,
        IDiagnosticLog? diagnosticLog = null)
        : this(
            () => CodexAppServerConnection.Start(),
            refreshInterval,
            timeProvider,
            reconnectBackoff: null,
            diagnosticLog)
    {
    }

    internal CodexRateLimitMonitor(
        Func<ICodexAppServerConnection> connectionFactory,
        TimeSpan? refreshInterval = null,
        TimeProvider? timeProvider = null,
        IReadOnlyList<TimeSpan>? reconnectBackoff = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _diagnosticLog = diagnosticLog ?? NullDiagnosticLog.Instance;
        _reconnectBackoff = reconnectBackoff ?? DefaultReconnectBackoff;
        if (_reconnectBackoff.Count == 0 || _reconnectBackoff.Any(delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectBackoff));
        }

        var initialRefreshInterval = refreshInterval ?? DefaultRefreshInterval;
        if (initialRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        }

        RefreshInterval = initialRefreshInterval;
        CurrentSnapshot = CreateStatusSnapshot(ConnectionState.Disconnected, diagnosticMessage: null);
        Diagnostics = RateLimitMonitorDiagnostics.Empty;
    }

    public event EventHandler<RateLimitSnapshot>? SnapshotChanged;

    public event EventHandler<RateLimitMonitorDiagnostics>? DiagnosticsChanged;

    public RateLimitSnapshot CurrentSnapshot { get; private set; }

    public RateLimitMonitorDiagnostics Diagnostics { get; private set; }

    public TimeSpan RefreshInterval
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _refreshIntervalTicks));
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Interlocked.Exchange(ref _refreshIntervalTicks, value.Ticks);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
        PublishStatus(ConnectionState.Connecting, "Подключение к Codex App Server…", preserveData: false);
        _diagnosticLog.Write(
            DiagnosticSeverity.Information,
            "Codex",
            "monitor.start",
            "Монитор лимитов запущен.");
        _supervisorTask = SuperviseAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = Volatile.Read(ref _connection)
            ?? throw new InvalidOperationException("Codex App Server is not connected.");
        await ReadFullSnapshotAsync(connection, cancellationToken);
    }

    public void RequestReconnect()
    {
        if (!_disposed)
        {
            _reconnectSignals.Writer.TryWrite(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _reconnectSignals.Writer.TryComplete();
        await IgnoreCancellationAsync(_supervisorTask);

        _snapshotGate.Dispose();
        _lifetime.Dispose();
        _diagnosticLog.Write(
            DiagnosticSeverity.Information,
            "Codex",
            "monitor.stop",
            "Монитор лимитов остановлен.");
    }

    private async Task SuperviseAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            ICodexAppServerConnection? connection = null;
            var retryDelay = TimeSpan.Zero;
            try
            {
                var attempts = Interlocked.Increment(ref _connectionAttempts);
                PublishDiagnostics(Diagnostics with { ConnectionAttempts = attempts });
                _diagnosticLog.Write(
                    DiagnosticSeverity.Information,
                    "Codex",
                    "connection.attempt",
                    "Запуск Codex App Server.");

                connection = _connectionFactory();
                await RunConnectedSessionAsync(
                    connection,
                    () => consecutiveFailures = 0,
                    cancellationToken);
                throw new IOException("Codex App Server session ended unexpectedly.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ReconnectRequestedException)
            {
                consecutiveFailures = 0;
                _diagnosticLog.Write(
                    DiagnosticSeverity.Information,
                    "Codex",
                    "connection.reconnect_requested",
                    "Запрошено повторное подключение.");
                PublishStatus(ConnectionState.Reconnecting, "Повторное подключение к Codex…", preserveData: true);
            }
            catch (NotSignedInException)
            {
                consecutiveFailures = 0;
                PublishStatus(
                    ConnectionState.NotSignedIn,
                    "Откройте Codex и войдите в аккаунт, затем нажмите обновить.",
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Warning,
                    "Codex",
                    "account.not_signed_in",
                    "Codex не авторизован.");
                retryDelay = TimeSpan.FromSeconds(30);
            }
            catch (CodexDiscoveryException exception)
            {
                consecutiveFailures = 0;
                PublishStatus(
                    ConnectionState.CodexNotFound,
                    exception.UserMessage,
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Error,
                    "Codex",
                    "process.not_found",
                    "Команда Codex не найдена.");
                retryDelay = TimeSpan.FromSeconds(30);
            }
            catch (FileNotFoundException)
            {
                consecutiveFailures = 0;
                PublishStatus(
                    ConnectionState.CodexNotFound,
                    "Codex не найден. Установите Codex CLI и добавьте команду codex в PATH.",
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Error,
                    "Codex",
                    "process.not_found",
                    "Команда Codex не найдена.");
                retryDelay = TimeSpan.FromSeconds(30);
            }
            catch (CodexAppServerUnavailableException)
            {
                consecutiveFailures++;
                RegisterReconnect();
                retryDelay = GetReconnectDelay(consecutiveFailures);
                PublishStatus(
                    ConnectionState.AppServerUnavailable,
                    $"Codex найден, но App Server не запустился. Повтор через {FormatDelay(retryDelay)}.",
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Warning,
                    "Codex",
                    "app_server.unavailable",
                    "Codex найден, но App Server недоступен; запланирован повтор.");
            }
            catch (RateLimitsUnavailableException)
            {
                consecutiveFailures++;
                RegisterReconnect();
                retryDelay = GetReconnectDelay(consecutiveFailures);
                PublishStatus(
                    ConnectionState.RateLimitsUnavailable,
                    $"Codex подключён, но лимиты временно недоступны. Повтор через {FormatDelay(retryDelay)}.",
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Warning,
                    "Codex",
                    "rate_limits.unavailable",
                    "Первичный snapshot лимитов недоступен; запланирован повтор.");
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                RegisterReconnect();
                retryDelay = GetReconnectDelay(consecutiveFailures);
                PublishStatus(
                    ConnectionState.Reconnecting,
                    $"Соединение потеряно. Повтор через {FormatDelay(retryDelay)}.",
                    preserveData: true);
                _diagnosticLog.Write(
                    DiagnosticSeverity.Warning,
                    "Codex",
                    "connection.lost",
                    $"Соединение потеряно ({exception.GetType().Name}); запланировано восстановление.");
            }
            finally
            {
                if (connection is not null)
                {
                    Interlocked.CompareExchange(ref _connection, null, connection);
                    await connection.DisposeAsync();
                    UpdateTransportDiagnostics(connection);
                }
            }

            if (retryDelay > TimeSpan.Zero)
            {
                await WaitBeforeRetryAsync(retryDelay, cancellationToken);
            }
        }
    }

    private async Task RunConnectedSessionAsync(
        ICodexAppServerConnection connection,
        Action onConnected,
        CancellationToken cancellationToken)
    {
        var updates = Channel.CreateUnbounded<JsonElement>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        void NotificationHandler(RpcNotification notification)
        {
            if (notification.Method == "account/rateLimits/updated" &&
                notification.Parameters is { } parameters)
            {
                updates.Writer.TryWrite(parameters);
            }
        }

        connection.Client.NotificationReceived += NotificationHandler;
        Task? updateTask = null;
        try
        {
            try
            {
                await InitializeSessionAsync(connection, cancellationToken);
            }
            catch (NotSignedInException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableProtocolFailure(exception))
            {
                throw new CodexAppServerUnavailableException(exception);
            }

            try
            {
                await ReadFullSnapshotAsync(connection, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableProtocolFailure(exception))
            {
                throw new RateLimitsUnavailableException(exception);
            }
            onConnected();
            Interlocked.Exchange(ref _connection, connection);

            var now = _timeProvider.GetUtcNow();
            PublishDiagnostics(Diagnostics with { ConnectedAt = now });
            _diagnosticLog.Write(
                DiagnosticSeverity.Information,
                "Codex",
                "connection.connected",
                "Соединение с Codex App Server установлено.");

            updateTask = ProcessUpdatesAsync(updates.Reader, cancellationToken);
            await MonitorConnectedSessionAsync(connection, cancellationToken);
        }
        finally
        {
            connection.Client.NotificationReceived -= NotificationHandler;
            updates.Writer.TryComplete();
            await IgnoreCancellationAsync(updateTask);
        }
    }

    private static async Task InitializeSessionAsync(
        ICodexAppServerConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.Client.SendRequestAsync<JsonElement>(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex-limit-monitor",
                    title = "Codex Limit Monitor",
                    version = "0.1.0",
                },
                capabilities = new { experimentalApi = true },
            },
            cancellationToken);
        await connection.Client.SendNotificationAsync("initialized", cancellationToken);

        var account = await connection.Client.SendRequestAsync<JsonElement>(
            "account/read",
            new { refreshToken = false },
            cancellationToken);
        if (!HasSignedInAccount(account))
        {
            throw new NotSignedInException();
        }
    }

    private async Task MonitorConnectedSessionAsync(
        ICodexAppServerConnection connection,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var refreshDelay = Task.Delay(RefreshInterval, _timeProvider, cycle.Token);
            var reconnectSignal = _reconnectSignals.Reader.WaitToReadAsync(cycle.Token).AsTask();
            var completed = await Task.WhenAny(refreshDelay, reconnectSignal, connection.Completion);
            await cycle.CancelAsync();

            if (completed == connection.Completion)
            {
                await connection.Completion;
                throw new IOException("Codex App Server process exited.");
            }

            if (completed == reconnectSignal && await reconnectSignal)
            {
                while (_reconnectSignals.Reader.TryRead(out _))
                {
                }

                throw new ReconnectRequestedException();
            }

            await ReadFullSnapshotAsync(connection, cancellationToken);
            UpdateTransportDiagnostics(connection);
        }
    }

    private async Task ReadFullSnapshotAsync(
        ICodexAppServerConnection connection,
        CancellationToken cancellationToken)
    {
        var response = await connection.Client.SendRequestAsync<GetAccountRateLimitsResponseDto>(
            "account/rateLimits/read",
            parameters: null,
            cancellationToken);

        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            _baseline = response;
            PublishNormalized(response);
            PublishDiagnostics(Diagnostics with { LastSuccessfulRefresh = _timeProvider.GetUtcNow() });
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private async Task ProcessUpdatesAsync(
        ChannelReader<JsonElement> updates,
        CancellationToken cancellationToken)
    {
        await foreach (var parameters in updates.ReadAllAsync(cancellationToken))
        {
            AccountRateLimitsUpdatedNotificationDto? update;
            try
            {
                update = parameters.Deserialize(
                    RateLimitDtoJsonContext.Default.AccountRateLimitsUpdatedNotificationDto);
            }
            catch (JsonException)
            {
                _diagnosticLog.Write(
                    DiagnosticSeverity.Warning,
                    "Protocol",
                    "notification.invalid",
                    "Некорректное уведомление лимитов пропущено.");
                continue;
            }

            if (update?.RateLimits is null)
            {
                continue;
            }

            await _snapshotGate.WaitAsync(cancellationToken);
            try
            {
                if (_baseline is null)
                {
                    continue;
                }

                _baseline = SparseRateLimitMerger.ApplyUpdate(_baseline, update);
                PublishNormalized(_baseline);
            }
            finally
            {
                _snapshotGate.Release();
            }
        }
    }

    private async Task WaitBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var retry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, _timeProvider, retry.Token);
        var signalTask = _reconnectSignals.Reader.WaitToReadAsync(retry.Token).AsTask();
        var completed = await Task.WhenAny(delayTask, signalTask);
        await retry.CancelAsync();
        if (completed == signalTask && await signalTask)
        {
            while (_reconnectSignals.Reader.TryRead(out _))
            {
            }
        }
    }

    private TimeSpan GetReconnectDelay(int consecutiveFailures)
    {
        var index = Math.Clamp(consecutiveFailures - 1, 0, _reconnectBackoff.Count - 1);
        return _reconnectBackoff[index];
    }

    private void RegisterReconnect()
    {
        var reconnects = Interlocked.Increment(ref _reconnectCount);
        PublishDiagnostics(Diagnostics with { ReconnectCount = reconnects });
    }

    private static bool IsRecoverableProtocolFailure(Exception exception) =>
        exception is IOException or TimeoutException or JsonException or RpcException or InvalidOperationException;

    private void UpdateTransportDiagnostics(ICodexAppServerConnection connection) =>
        PublishDiagnostics(Diagnostics with
        {
            MalformedLineCount = connection.Client.MalformedLineCount,
            StderrLineCount = connection.StderrLineCount,
        });

    private void PublishNormalized(GetAccountRateLimitsResponseDto response)
    {
        var snapshot = RateLimitNormalizer.Normalize(
            RateLimitResponseMapper.ToSourceSnapshots(response),
            _timeProvider.GetUtcNow(),
            ConnectionState.Connected);
        Publish(snapshot);
    }

    private void PublishStatus(
        ConnectionState state,
        string? diagnosticMessage,
        bool preserveData)
    {
        var snapshot = preserveData && CurrentSnapshot.HasData
            ? CurrentSnapshot with { State = state, DiagnosticMessage = diagnosticMessage }
            : CreateStatusSnapshot(state, diagnosticMessage);
        Publish(snapshot);
    }

    private void Publish(RateLimitSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void PublishDiagnostics(RateLimitMonitorDiagnostics diagnostics)
    {
        Diagnostics = diagnostics;
        DiagnosticsChanged?.Invoke(this, diagnostics);
    }

    private RateLimitSnapshot CreateStatusSnapshot(
        ConnectionState state,
        string? diagnosticMessage) =>
        new(
            Buckets: [],
            PlanType: null,
            RateLimitReachedType: null,
            RetrievedAt: _timeProvider.GetUtcNow(),
            State: state,
            DiagnosticMessage: diagnosticMessage);

    private static bool HasSignedInAccount(JsonElement accountResult) =>
        accountResult.ValueKind == JsonValueKind.Object &&
        accountResult.TryGetProperty("account", out var account) &&
        account.ValueKind == JsonValueKind.Object;

    private static string FormatDelay(TimeSpan delay) => delay.TotalSeconds < 1
        ? "мгновение"
        : $"{delay.TotalSeconds:0} сек";

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class NotSignedInException : Exception;

    private sealed class RateLimitsUnavailableException(Exception innerException)
        : Exception("Rate limits are unavailable.", innerException);

    private sealed class ReconnectRequestedException : Exception;
}
