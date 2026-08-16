using CodexLimitMonitor.App.Services;

namespace CodexLimitMonitor.App.ViewModels;

internal sealed class SettingsViewModel : ObservableObject
{
    private double _opacityPercent;
    private double _refreshIntervalSeconds;
    private bool _showResetCountdown;
    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _isCompact;
    private bool _isTopmost;

    public SettingsViewModel(AppSettings settings)
    {
        _opacityPercent = settings.Opacity * 100;
        _refreshIntervalSeconds = settings.RefreshIntervalSeconds;
        _showResetCountdown = settings.ShowResetCountdown;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _isCompact = settings.IsCompact;
        _isTopmost = settings.IsTopmost;
    }

    public double OpacityPercent
    {
        get => _opacityPercent;
        set => SetProperty(ref _opacityPercent, value);
    }

    public double RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set => SetProperty(ref _refreshIntervalSeconds, value);
    }

    public bool ShowResetCountdown
    {
        get => _showResetCountdown;
        set => SetProperty(ref _showResetCountdown, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }

    public bool IsTopmost
    {
        get => _isTopmost;
        set => SetProperty(ref _isTopmost, value);
    }

    public AppSettings ApplyTo(AppSettings settings) => (settings with
    {
        Opacity = OpacityPercent / 100,
        RefreshIntervalSeconds = (int)Math.Round(RefreshIntervalSeconds),
        ShowResetCountdown = ShowResetCountdown,
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        IsCompact = IsCompact,
        IsTopmost = IsTopmost,
    }).Normalize();
}
