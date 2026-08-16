using System.Windows.Media;
using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.App.ViewModels;

internal sealed class RateLimitBucketViewModel : ObservableObject
{
    private static readonly Brush HealthyBrush = CreateBrush("#FF72E6A1");
    private static readonly Brush WarningBrush = CreateBrush("#FFFFC857");
    private static readonly Brush CriticalBrush = CreateBrush("#FFFF667A");

    private string _displayName = string.Empty;
    private double _remainingPercent;
    private DateTimeOffset? _resetsAt;
    private string _countdownText = string.Empty;
    private Brush _accentBrush = HealthyBrush;

    public RateLimitBucketViewModel(RateLimitBucket bucket)
    {
        Key = bucket.Key;
        Update(bucket);
    }

    public string Key { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public double RemainingPercent
    {
        get => _remainingPercent;
        private set
        {
            if (SetProperty(ref _remainingPercent, value))
            {
                OnPropertyChanged(nameof(PercentageText));
            }
        }
    }

    public string PercentageText => $"{RemainingPercent:0}%";

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetProperty(ref _accentBrush, value);
    }

    public void Update(RateLimitBucket bucket)
    {
        DisplayName = bucket.DisplayName;
        RemainingPercent = bucket.RemainingPercent;
        _resetsAt = bucket.ResetsAt;
        AccentBrush = RemainingPercent switch
        {
            < 10 => CriticalBrush,
            <= 30 => WarningBrush,
            _ => HealthyBrush,
        };
        UpdateCountdown(DateTimeOffset.Now);
    }

    public void UpdateCountdown(DateTimeOffset now)
    {
        if (_resetsAt is null)
        {
            CountdownText = "Время сброса неизвестно";
            return;
        }

        var remaining = _resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            CountdownText = "Сброс сейчас";
            return;
        }

        CountdownText = remaining.TotalDays >= 1
            ? $"Сброс через {(int)remaining.TotalDays}д {remaining.Hours}ч"
            : remaining.TotalHours >= 1
                ? $"Сброс через {(int)remaining.TotalHours}ч {remaining.Minutes}м"
                : $"Сброс через {Math.Max(1, remaining.Minutes)}м";
    }

    private static Brush CreateBrush(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
