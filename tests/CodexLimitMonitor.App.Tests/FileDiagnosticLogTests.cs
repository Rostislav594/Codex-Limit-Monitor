using CodexLimitMonitor.App.Services;
using CodexLimitMonitor.Core.Models;
using System.IO;

namespace CodexLimitMonitor.App.Tests;

public sealed class FileDiagnosticLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexLimitMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteRedactsCredentialsAndPersonalIdentifiers()
    {
        var path = Path.Combine(_directory, "monitor.jsonl");
        var log = new FileDiagnosticLog(path);

        log.Write(
            DiagnosticSeverity.Warning,
            "Protocol",
            "request.failed",
            "authorization: Bearer secret-value user@example.com access_token=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGH");

        var contents = File.ReadAllText(path);
        Assert.DoesNotContain("secret-value", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGH", contents, StringComparison.Ordinal);
        Assert.Contains("[redacted", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentEntriesContainOnlySanitizedMessages()
    {
        var log = new FileDiagnosticLog(Path.Combine(_directory, "monitor.jsonl"));

        log.Write(DiagnosticSeverity.Information, "Account", "state", "cookie=my-private-cookie-value");

        var entry = Assert.Single(log.RecentEntries);
        Assert.Equal("cookie [redacted-secret]", entry.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
