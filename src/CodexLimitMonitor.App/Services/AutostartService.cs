using Microsoft.Win32;

namespace CodexLimitMonitor.App.Services;

internal sealed class AutostartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Codex Limit Monitor";

    public AutostartRegistration Synchronize()
    {
        var executablePath = Environment.ProcessPath;
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
        var registeredCommand = key?.GetValue(
            ValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var state = EvaluateRegistration(registeredCommand, executablePath);

        if (state == AutostartRegistrationState.Stale)
        {
            key!.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }

        return new AutostartRegistration(
            IsEnabled: state != AutostartRegistrationState.Disabled,
            WasMigrated: state == AutostartRegistrationState.Stale);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key.SetValue(ValueName, BuildCommand(Environment.ProcessPath), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            if (key is null)
            {
                return;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static AutostartRegistrationState EvaluateRegistration(
        string? registeredCommand,
        string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(registeredCommand))
        {
            return AutostartRegistrationState.Disabled;
        }

        var expectedCommand = BuildCommand(executablePath);
        var isCurrent = string.Equals(
            registeredCommand.Trim(),
            expectedCommand,
            StringComparison.OrdinalIgnoreCase);
        return isCurrent
            ? AutostartRegistrationState.Current
            : AutostartRegistrationState.Stale;
    }

    internal static string BuildCommand(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The executable path is not available.");
        }

        return $"\"{executablePath}\" --autostart";
    }
}

internal readonly record struct AutostartRegistration(bool IsEnabled, bool WasMigrated);

internal enum AutostartRegistrationState
{
    Disabled,
    Current,
    Stale,
}
