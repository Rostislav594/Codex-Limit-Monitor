using System.Text;
using System.Text.Json;
using CodexLimitMonitor.Core.Abstractions;
using CodexLimitMonitor.Core.Models;

namespace CodexLimitMonitor.App.Services;

internal sealed class FileDiagnosticLog : IDiagnosticLog
{
    private const long MaximumLogSize = 1_048_576;
    private const int MaximumRecentEntries = 200;

    private readonly object _sync = new();
    private readonly Queue<DiagnosticEntry> _recentEntries = new();

    public FileDiagnosticLog(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexLimitMonitor",
            "Logs",
            "monitor.jsonl");
    }

    public event EventHandler? EntriesChanged;

    public IReadOnlyList<DiagnosticEntry> RecentEntries
    {
        get
        {
            lock (_sync)
            {
                return _recentEntries.ToArray();
            }
        }
    }

    public string FilePath { get; }

    string? IDiagnosticLog.FilePath => FilePath;

    public void Write(
        DiagnosticSeverity severity,
        string source,
        string eventName,
        string message)
    {
        var entry = new DiagnosticEntry(
            DateTimeOffset.Now,
            severity,
            DiagnosticSanitizer.Sanitize(source),
            DiagnosticSanitizer.Sanitize(eventName),
            DiagnosticSanitizer.Sanitize(message));

        lock (_sync)
        {
            _recentEntries.Enqueue(entry);
            while (_recentEntries.Count > MaximumRecentEntries)
            {
                _recentEntries.Dequeue();
            }

            TryAppend(entry);
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryAppend(DiagnosticEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidOperationException("Log path does not have a parent directory.");
            Directory.CreateDirectory(directory);
            RotateIfRequired();
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            File.AppendAllText(FilePath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void RotateIfRequired()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaximumLogSize)
        {
            return;
        }

        File.Move(FilePath, FilePath + ".previous", overwrite: true);
    }
}
