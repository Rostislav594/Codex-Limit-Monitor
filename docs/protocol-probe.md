# Protocol probe

`CodexLimitMonitor.ProtocolProbe` is the Stage 1 console prototype for the
Codex Limit Monitor project. It proves the complete local App Server lifecycle
before any UI code is introduced.

## Run

```powershell
dotnet run --project .\src\CodexLimitMonitor.ProtocolProbe\CodexLimitMonitor.ProtocolProbe.csproj
```

The probe performs the following operations:

1. Locates `codex.cmd` or `codex.exe` on `PATH`.
2. Starts `codex app-server --stdio` with redirected standard streams.
3. Sends `initialize` followed by the `initialized` notification.
4. Calls `account/read` and `account/rateLimits/read`.
5. Deserializes the response through the production DTO and Core normalizer.
6. Prints a structural summary and shuts the child process down.

To use an explicit Codex command path:

```powershell
$env:CODEX_LIMIT_MONITOR_CODEX_PATH = 'C:\path\to\codex.cmd'
dotnet run --project .\src\CodexLimitMonitor.ProtocolProbe\CodexLimitMonitor.ProtocolProbe.csproj
```

## Output safety

The probe does not print account email, plan, credit balance, percentages,
reset timestamps, named limit IDs, raw notifications, stderr content, or RPC
error messages. It reports only protocol structure and success state.

Exit codes:

- `0`: the complete probe succeeded;
- `1`: startup or protocol failure;
- `2`: cancelled by the user.
