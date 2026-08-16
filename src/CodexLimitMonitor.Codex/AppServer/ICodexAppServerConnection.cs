using CodexLimitMonitor.Codex.Protocol;

namespace CodexLimitMonitor.Codex.AppServer;

internal interface ICodexAppServerConnection : IAsyncDisposable
{
    JsonlRpcClient Client { get; }

    Task Completion { get; }

    int StderrLineCount { get; }
}
