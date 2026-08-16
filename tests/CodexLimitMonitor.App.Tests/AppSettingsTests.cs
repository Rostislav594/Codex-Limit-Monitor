using CodexLimitMonitor.App.Services;

namespace CodexLimitMonitor.App.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void NormalizeClampsUnsafeValuesAndRemovesInvalidCoordinates()
    {
        var settings = new AppSettings
        {
            Version = 99,
            WindowLeft = double.NaN,
            WindowTop = double.PositiveInfinity,
            Opacity = 0.1,
            RefreshIntervalSeconds = 2,
        };

        var normalized = settings.Normalize();

        Assert.Equal(AppSettings.CurrentVersion, normalized.Version);
        Assert.Null(normalized.WindowLeft);
        Assert.Null(normalized.WindowTop);
        Assert.Equal(0.35, normalized.Opacity);
        Assert.Equal(15, normalized.RefreshIntervalSeconds);
    }

    [Fact]
    public void AutostartCommandQuotesExecutablePath()
    {
        var command = AutostartService.BuildCommand(@"C:\Program Files\Codex Limit Monitor\monitor.exe");

        Assert.Equal("\"C:\\Program Files\\Codex Limit Monitor\\monitor.exe\" --autostart", command);
    }

    [Fact]
    public void MissingAutostartRegistrationIsDisabled()
    {
        var state = AutostartService.EvaluateRegistration(
            registeredCommand: null,
            executablePath: @"C:\Apps\CodexLimitMonitor.exe");

        Assert.Equal(AutostartRegistrationState.Disabled, state);
    }

    [Fact]
    public void CurrentAutostartRegistrationDoesNotNeedMigration()
    {
        var state = AutostartService.EvaluateRegistration(
            registeredCommand: "  \"C:\\APPS\\CodexLimitMonitor.exe\" --autostart  ",
            executablePath: @"C:\Apps\CodexLimitMonitor.exe");

        Assert.Equal(AutostartRegistrationState.Current, state);
    }

    [Theory]
    [InlineData("\"C:\\Old Location\\CodexLimitMonitor.exe\" --autostart")]
    [InlineData("\"C:\\Apps\\CodexLimitMonitor.exe\"")]
    [InlineData("CodexLimitMonitor.exe --autostart")]
    public void StaleOrMalformedAutostartRegistrationNeedsMigration(string registeredCommand)
    {
        var state = AutostartService.EvaluateRegistration(
            registeredCommand,
            executablePath: @"C:\Apps\CodexLimitMonitor.exe");

        Assert.Equal(AutostartRegistrationState.Stale, state);
    }
}
