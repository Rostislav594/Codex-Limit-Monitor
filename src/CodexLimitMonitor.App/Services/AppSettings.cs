namespace CodexLimitMonitor.App.Services;

internal sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public double? WindowLeft { get; init; }

    public double? WindowTop { get; init; }

    public bool IsCompact { get; init; }

    public bool IsTopmost { get; init; } = true;

    public bool IsClickThrough { get; init; }

    public double Opacity { get; init; } = 0.96;

    public int RefreshIntervalSeconds { get; init; } = 60;

    public bool ShowResetCountdown { get; init; } = true;

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; }

    public AppSettings Normalize() => this with
    {
        Version = CurrentVersion,
        WindowLeft = NormalizeCoordinate(WindowLeft),
        WindowTop = NormalizeCoordinate(WindowTop),
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.35, 1.0) : 0.96,
        RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 15, 3600),
    };

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
}
