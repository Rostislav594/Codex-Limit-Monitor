using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexLimitMonitor.App.Services;
using CodexLimitMonitor.Core.Abstractions;
using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.App.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly Brush ConnectedBrush = CreateBrush("#FF72E6A1");
    private static readonly Brush WorkingBrush = CreateBrush("#FFFFC857");
    private static readonly Brush ErrorBrush = CreateBrush("#FFFF667A");

    private readonly IRateLimitMonitor _monitor;
    private readonly DispatcherTimer _countdownTimer;
    private RateLimitBucketViewModel? _primaryBucket;
    private bool _isCompact;
    private bool _showResetCountdown = true;
    private string _statusText = "STARTING";
    private string? _diagnosticMessage;
    private Brush _statusBrush = WorkingBrush;

    public MainWindowViewModel(IRateLimitMonitor monitor, AppSettings settings)
    {
        _monitor = monitor;
        _isCompact = settings.IsCompact;
        _showResetCountdown = settings.ShowResetCountdown;
        _monitor.SnapshotChanged += OnSnapshotChanged;
        ToggleModeCommand = new RelayCommand(() => IsCompact = !IsCompact);
        RefreshCommand = new AsyncRelayCommand(() => _monitor.RefreshAsync(), OnRefreshFailed);

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();
        _countdownTimer.Start();
    }

    public ObservableCollection<RateLimitBucketViewModel> Buckets { get; } = [];

    public ObservableCollection<RateLimitBucketViewModel> SecondaryBuckets { get; } = [];

    public RateLimitBucketViewModel? PrimaryBucket
    {
        get => _primaryBucket;
        private set
        {
            if (SetProperty(ref _primaryBucket, value))
            {
                OnPropertyChanged(nameof(HasData));
            }
        }
    }

    public bool HasData => PrimaryBucket is not null;

    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (SetProperty(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(WindowWidth));
                OnPropertyChanged(nameof(WindowHeight));
            }
        }
    }

    public double WindowWidth => IsCompact ? 238 : 326;

    public double WindowHeight => IsCompact ? 100 : 302 + (SecondaryBuckets.Count * 58);

    public bool ShowResetCountdown
    {
        get => _showResetCountdown;
        set => SetProperty(ref _showResetCountdown, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public string? DiagnosticMessage
    {
        get => _diagnosticMessage;
        private set => SetProperty(ref _diagnosticMessage, value);
    }

    public ICommand ToggleModeCommand { get; }

    public ICommand RefreshCommand { get; }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _monitor.StartAsync(cancellationToken);

    public void Dispose()
    {
        _countdownTimer.Stop();
        _monitor.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, RateLimitSnapshot snapshot)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
        }
    }

    private void ApplySnapshot(RateLimitSnapshot snapshot)
    {
        SynchronizeBuckets(snapshot.Buckets);
        PrimaryBucket = Buckets.FirstOrDefault();

        SecondaryBuckets.Clear();
        foreach (var bucket in Buckets.Skip(1))
        {
            SecondaryBuckets.Add(bucket);
        }

        OnPropertyChanged(nameof(WindowHeight));
        DiagnosticMessage = snapshot.DiagnosticMessage;
        (StatusText, StatusBrush) = snapshot.State switch
        {
            ConnectionState.Connected => ("LIVE", ConnectedBrush),
            ConnectionState.Connecting => ("CONNECTING", WorkingBrush),
            ConnectionState.Reconnecting => ("RECONNECTING", WorkingBrush),
            ConnectionState.NoRateLimitData => ("NO DATA", WorkingBrush),
            ConnectionState.NotSignedIn => ("SIGN IN", ErrorBrush),
            ConnectionState.CodexNotFound => ("CODEX MISSING", ErrorBrush),
            ConnectionState.AppServerUnavailable => ("APP SERVER", ErrorBrush),
            ConnectionState.RateLimitsUnavailable => ("LIMITS ERROR", ErrorBrush),
            _ => ("OFFLINE", ErrorBrush),
        };
    }

    private void SynchronizeBuckets(IReadOnlyList<RateLimitBucket> sourceBuckets)
    {
        for (var index = 0; index < sourceBuckets.Count; index++)
        {
            var source = sourceBuckets[index];
            var existing = Buckets.FirstOrDefault(bucket => bucket.Key == source.Key);
            if (existing is null)
            {
                existing = new RateLimitBucketViewModel(source);
                Buckets.Insert(index, existing);
            }
            else
            {
                existing.Update(source);
                var currentIndex = Buckets.IndexOf(existing);
                if (currentIndex != index)
                {
                    Buckets.Move(currentIndex, index);
                }
            }
        }

        while (Buckets.Count > sourceBuckets.Count)
        {
            Buckets.RemoveAt(Buckets.Count - 1);
        }
    }

    private void UpdateCountdowns()
    {
        var now = DateTimeOffset.Now;
        foreach (var bucket in Buckets)
        {
            bucket.UpdateCountdown(now);
        }
    }

    private void OnRefreshFailed(Exception exception)
    {
        DiagnosticMessage = "Не удалось обновить лимиты. Монитор попробует подключиться снова.";
    }

    private static Brush CreateBrush(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
