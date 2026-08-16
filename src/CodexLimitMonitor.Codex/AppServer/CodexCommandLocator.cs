using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace CodexLimitMonitor.Codex.AppServer;

public static class CodexCommandLocator
{
    public const string PathEnvironmentVariable = "CODEX_LIMIT_MONITOR_CODEX_PATH";

    private static readonly string[] WindowsCommandNames = ["codex.exe", "codex.cmd", "codex.bat"];

    public static ProcessStartInfo CreateAppServerStartInfo()
    {
        var commandPath = ResolveCodexPath();
        var isCommandScript = IsCommandScript(commandPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = isCommandScript
                ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
                : commandPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (isCommandScript)
        {
            startInfo.Arguments = $"/d /s /c \"\"{commandPath}\" app-server --stdio\"";
        }
        else
        {
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        return startInfo;
    }

    public static string ResolveCodexPath() => ResolveCodexPath(
        Environment.GetEnvironmentVariable(PathEnvironmentVariable),
        GetPathDirectories(),
        GetFallbackCandidates());

    internal static string ResolveCodexPath(
        string? configuredPath,
        IEnumerable<string> pathDirectories,
        IEnumerable<string> fallbackCandidates)
    {
        ArgumentNullException.ThrowIfNull(pathDirectories);
        ArgumentNullException.ThrowIfNull(fallbackCandidates);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var explicitPath = NormalizeCandidate(configuredPath);
            if (explicitPath is not null && IsSupportedCommand(explicitPath))
            {
                return explicitPath;
            }

            throw new CodexDiscoveryException(
                $"Путь из {PathEnvironmentVariable} недействителен. Исправьте переменную или удалите её.");
        }

        var commandNames = OperatingSystem.IsWindows() ? WindowsCommandNames : ["codex"];
        foreach (var directory in pathDirectories.Where(Directory.Exists))
        {
            foreach (var commandName in commandNames)
            {
                var resolved = NormalizeCandidate(Path.Combine(directory, commandName));
                if (resolved is not null && IsSupportedCommand(resolved))
                {
                    return resolved;
                }
            }
        }

        foreach (var candidate in fallbackCandidates)
        {
            var resolved = NormalizeCandidate(candidate);
            if (resolved is not null && IsSupportedCommand(resolved))
            {
                return resolved;
            }
        }

        throw new CodexDiscoveryException(
            "Codex не найден. Установите Codex CLI и убедитесь, что команда codex доступна в PATH.");
    }

    private static IEnumerable<string> GetPathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(path => path.Trim('"'))
        .Where(path => !string.IsNullOrWhiteSpace(path));

    private static IEnumerable<string> GetFallbackCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "npm", "codex.cmd");
            yield return Path.Combine(appData, "npm", "codex.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "codex.exe");
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe");
            yield return Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            yield return Path.Combine(localAppData, "Programs", "Codex", "codex.exe");
        }

        foreach (var registeredPath in GetRegisteredAppPaths())
        {
            yield return registeredPath;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> GetRegisteredAppPaths()
    {
        const string subkey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\codex.exe";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            string? registeredPath = null;
            try
            {
                using var key = root.OpenSubKey(subkey, writable: false);
                registeredPath = key?.GetValue(name: null) as string;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            if (!string.IsNullOrWhiteSpace(registeredPath))
            {
                yield return registeredPath;
            }
        }
    }

    private static string? NormalizeCandidate(string candidate)
    {
        try
        {
            var trimmed = candidate.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(trimmed) ? null : Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSupportedCommand(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommandScript(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }
}
