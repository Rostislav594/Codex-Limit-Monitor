using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using CodexLimitMonitor.Core.Abstractions;
using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.App.ViewModels;

internal sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly IRateLimitMonitor _monitor;
    private readonly IDiagnosticLog _log;
    private string _stateText = "Запуск";
    private string _lastRefreshText = "—";
    private int _bucketCount;
    private int _connectionAttempts;
    private int _reconnectCount;
    private int _protocolIssues;
    private int _stderrLines;

    public DiagnosticsViewModel(IRateLimitMonitor monitor, IDiagnosticLog log)
    {
        _monitor = monitor;
        _log = log;
        _monitor.SnapshotChanged += OnSnapshotChanged;
        _monitor.DiagnosticsChanged += OnDiagnosticsChanged;
        _log.EntriesChanged += OnLogEntriesChanged;
        ReconnectCommand = new RelayCommand(_monitor.RequestReconnect);
        CopySummaryCommand = new RelayCommand(CopySummary);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder, () => _log.FilePath is not null);
        RefreshAll();
    }

    public ObservableCollection<string> RecentLogLines { get; } = [];

    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetProperty(ref _lastRefreshText, value);
    }

    public int BucketCount
    {
        get => _bucketCount;
        private set => SetProperty(ref _bucketCount, value);
    }

    public int ConnectionAttempts
    {
        get => _connectionAttempts;
        private set => SetProperty(ref _connectionAttempts, value);
    }

    public int ReconnectCount
    {
        get => _reconnectCount;
        private set => SetProperty(ref _reconnectCount, value);
    }

    public int ProtocolIssues
    {
        get => _protocolIssues;
        private set => SetProperty(ref _protocolIssues, value);
    }

    public int StderrLines
    {
        get => _stderrLines;
        private set => SetProperty(ref _stderrLines, value);
    }

    public string LogPath => _log.FilePath ?? "Локальный файл логов отключён";

    public ICommand ReconnectCommand { get; }

    public ICommand CopySummaryCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    public void Dispose()
    {
        _monitor.SnapshotChanged -= OnSnapshotChanged;
        _monitor.DiagnosticsChanged -= OnDiagnosticsChanged;
        _log.EntriesChanged -= OnLogEntriesChanged;
    }

    private void OnSnapshotChanged(object? sender, RateLimitSnapshot snapshot) => Dispatch(RefreshSnapshot);

    private void OnDiagnosticsChanged(object? sender, RateLimitMonitorDiagnostics diagnostics) =>
        Dispatch(RefreshDiagnostics);

    private void OnLogEntriesChanged(object? sender, EventArgs e) => Dispatch(RefreshLog);

    private void RefreshAll()
    {
        RefreshSnapshot();
        RefreshDiagnostics();
        RefreshLog();
    }

    private void RefreshSnapshot()
    {
        var snapshot = _monitor.CurrentSnapshot;
        StateText = snapshot.State switch
        {
            ConnectionState.Connected => "Подключено",
            ConnectionState.Connecting => "Подключение",
            ConnectionState.Reconnecting => "Восстановление",
            ConnectionState.CodexNotFound => "Codex не найден",
            ConnectionState.AppServerUnavailable => "App Server недоступен",
            ConnectionState.NotSignedIn => "Нет авторизации",
            ConnectionState.RateLimitsUnavailable => "Лимиты недоступны",
            ConnectionState.NoRateLimitData => "Нет данных лимитов",
            ConnectionState.Offline => "Нет сети",
            ConnectionState.ServerError => "Ошибка сервера",
            _ => "Отключено",
        };
        BucketCount = snapshot.Buckets.Count;
    }

    private void RefreshDiagnostics()
    {
        var diagnostics = _monitor.Diagnostics;
        ConnectionAttempts = diagnostics.ConnectionAttempts;
        ReconnectCount = diagnostics.ReconnectCount;
        ProtocolIssues = diagnostics.MalformedLineCount;
        StderrLines = diagnostics.StderrLineCount;
        LastRefreshText = diagnostics.LastSuccessfulRefresh?.LocalDateTime.ToString("g") ?? "—";
    }

    private void RefreshLog()
    {
        RecentLogLines.Clear();
        foreach (var entry in _log.RecentEntries.TakeLast(100))
        {
            RecentLogLines.Add(
                $"{entry.Timestamp.LocalDateTime:HH:mm:ss}  {SeverityCode(entry.Severity),-4}  {entry.EventName}  {entry.Message}");
        }
    }

    private void CopySummary()
    {
        var summary = $"Codex Limit Monitor\n" +
            $"State: {StateText}\n" +
            $"Buckets: {BucketCount}\n" +
            $"Last refresh: {LastRefreshText}\n" +
            $"Connection attempts: {ConnectionAttempts}\n" +
            $"Reconnects: {ReconnectCount}\n" +
            $"Malformed protocol lines: {ProtocolIssues}\n" +
            $"Stderr lines: {StderrLines}";
        System.Windows.Clipboard.SetText(summary);
    }

    private void OpenLogFolder()
    {
        if (_log.FilePath is not { } path)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    private static string SeverityCode(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "ERR",
        DiagnosticSeverity.Warning => "WARN",
        _ => "INFO",
    };

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }
}
