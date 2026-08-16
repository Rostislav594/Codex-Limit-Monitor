using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.Core.Abstractions;

public interface IDiagnosticLog
{
    event EventHandler? EntriesChanged;

    IReadOnlyList<DiagnosticEntry> RecentEntries { get; }

    string? FilePath { get; }

    void Write(
        DiagnosticSeverity severity,
        string source,
        string eventName,
        string message);
}

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static NullDiagnosticLog Instance { get; } = new();

    private NullDiagnosticLog()
    {
    }

    public event EventHandler? EntriesChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<DiagnosticEntry> RecentEntries => [];

    public string? FilePath => null;

    public void Write(
        DiagnosticSeverity severity,
        string source,
        string eventName,
        string message)
    {
    }
}
