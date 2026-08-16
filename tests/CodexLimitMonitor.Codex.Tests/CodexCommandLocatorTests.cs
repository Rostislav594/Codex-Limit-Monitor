using CodexLimitMonitor.Codex.AppServer;

namespace CodexLimitMonitor.Codex.Tests;

public sealed class CodexCommandLocatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexLimitMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExplicitPathTakesPrecedence()
    {
        var explicitCommand = CreateCommand("explicit", "codex.cmd");
        var pathCommandDirectory = Path.GetDirectoryName(CreateCommand("path", "codex.exe"))!;

        var resolved = CodexCommandLocator.ResolveCodexPath(
            explicitCommand,
            [pathCommandDirectory],
            []);

        Assert.Equal(Path.GetFullPath(explicitCommand), resolved);
    }

    [Fact]
    public void PathIsSearchedBeforeFallbackLocations()
    {
        var pathCommand = CreateCommand("path", "codex.cmd");
        var fallbackCommand = CreateCommand("fallback", "codex.exe");

        var resolved = CodexCommandLocator.ResolveCodexPath(
            configuredPath: null,
            [Path.GetDirectoryName(pathCommand)!],
            [fallbackCommand]);

        Assert.Equal(Path.GetFullPath(pathCommand), resolved);
    }

    [Fact]
    public void TypicalInstallLocationIsUsedWhenPathDoesNotContainCodex()
    {
        var fallbackCommand = CreateCommand("fallback", "codex.exe");

        var resolved = CodexCommandLocator.ResolveCodexPath(
            configuredPath: null,
            [],
            [fallbackCommand]);

        Assert.Equal(Path.GetFullPath(fallbackCommand), resolved);
    }

    [Fact]
    public void InvalidExplicitPathProducesActionableError()
    {
        var fallbackCommand = CreateCommand("fallback", "codex.exe");

        var exception = Assert.Throws<CodexDiscoveryException>(() =>
            CodexCommandLocator.ResolveCodexPath(
                Path.Combine(_directory, "missing", "codex.exe"),
                [],
                [fallbackCommand]));

        Assert.Contains(CodexCommandLocator.PathEnvironmentVariable, exception.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCodexProducesInstallGuidance()
    {
        var exception = Assert.Throws<CodexDiscoveryException>(() =>
            CodexCommandLocator.ResolveCodexPath(configuredPath: null, [], []));

        Assert.Contains("Установите Codex CLI", exception.UserMessage, StringComparison.Ordinal);
        Assert.Contains("PATH", exception.UserMessage, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        foreach (var directory in Directory.GetDirectories(_directory).OrderByDescending(path => path.Length))
        {
            Directory.Delete(directory);
        }

        Directory.Delete(_directory);
    }

    private string CreateCommand(string directoryName, string fileName)
    {
        var directory = Path.Combine(_directory, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        return path;
    }
}
