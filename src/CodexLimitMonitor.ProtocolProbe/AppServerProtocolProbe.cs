using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexLimitMonitor.Codex.Dto;
using CodexLimitMonitor.Codex.Protocol;
using CodexLimitMonitor.Codex.RateLimits;
using CodexLimitMonitor.Core.RateLimits;

namespace CodexLimitMonitor.ProtocolProbe;

internal sealed class AppServerProtocolProbe
{
    private readonly JsonlRpcClient _client;
    private readonly Func<int> _getStderrLineCount;
    private readonly ConcurrentDictionary<string, byte> _notificationMethods = new(StringComparer.Ordinal);

    public AppServerProtocolProbe(JsonlRpcClient client, Func<int> getStderrLineCount)
    {
        _client = client;
        _getStderrLineCount = getStderrLineCount;
        _client.NotificationReceived += notification => _notificationMethods.TryAdd(notification.Method, 0);
    }

    public async Task<ProbeSummary> RunAsync(CancellationToken cancellationToken)
    {
        var initializeResult = await _client.SendRequestAsync<JsonElement>(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex-limit-monitor",
                    title = "Codex Limit Monitor",
                    version = "0.1.0-probe",
                },
                capabilities = new { experimentalApi = true },
            },
            cancellationToken);

        await _client.SendNotificationAsync("initialized", cancellationToken);

        var accountResult = await _client.SendRequestAsync<JsonElement>(
            "account/read",
            new { refreshToken = false },
            cancellationToken);

        var rateLimitsResult = await _client.SendRequestAsync<JsonElement>(
            "account/rateLimits/read",
            parameters: null,
            cancellationToken);

        var typedRateLimits = rateLimitsResult.Deserialize(
            RateLimitDtoJsonContext.Default.GetAccountRateLimitsResponseDto)
            ?? throw new JsonException("Rate-limit response could not be deserialized.");
        var normalizedSnapshot = RateLimitNormalizer.Normalize(
            RateLimitResponseMapper.ToSourceSnapshots(typedRateLimits),
            DateTimeOffset.UtcNow);

        return CreateSafeSummary(
            initializeResult,
            accountResult,
            rateLimitsResult,
            normalizedSnapshot.Buckets.Count,
            normalizedSnapshot.State.ToString());
    }

    private ProbeSummary CreateSafeSummary(
        JsonElement initializeResult,
        JsonElement accountResult,
        JsonElement rateLimitsResult,
        int normalizedBucketCount,
        string normalizationState)
    {
        var account = GetObject(accountResult, "account");
        var legacySnapshot = GetObject(rateLimitsResult, "rateLimits");
        var primary = GetObject(legacySnapshot, "primary");
        var secondary = GetObject(legacySnapshot, "secondary");
        var rateLimitsById = GetObject(rateLimitsResult, "rateLimitsByLimitId");

        return new ProbeSummary(
            Initialize: new InitializeSummary(
                Succeeded: true,
                PlatformFamily: GetString(initializeResult, "platformFamily"),
                PlatformOs: GetString(initializeResult, "platformOs"),
                UserAgent: GetString(initializeResult, "userAgent"),
                ResponseFields: GetObjectFieldNames(initializeResult)),
            AccountRead: new AccountSummary(
                Succeeded: true,
                AccountPresent: account.ValueKind == JsonValueKind.Object,
                AccountType: GetString(account, "type"),
                ResponseFields: GetObjectFieldNames(accountResult)),
            RateLimitsRead: new RateLimitsSummary(
                Succeeded: true,
                ResponseFields: GetObjectFieldNames(rateLimitsResult),
                LegacySnapshotFields: GetObjectFieldNames(legacySnapshot),
                PrimaryPresent: primary.ValueKind == JsonValueKind.Object,
                PrimaryFields: GetObjectFieldNames(primary),
                SecondaryPresent: secondary.ValueKind == JsonValueKind.Object,
                SecondaryFields: GetObjectFieldNames(secondary),
                NamedLimitMapPresent: rateLimitsById.ValueKind == JsonValueKind.Object,
                NamedLimitCount: rateLimitsById.ValueKind == JsonValueKind.Object
                    ? rateLimitsById.EnumerateObject().Count()
                    : 0,
                NormalizedBucketCount: normalizedBucketCount,
                NormalizationState: normalizationState),
            NotificationMethodsSeen: _notificationMethods.Keys.Order(StringComparer.Ordinal).ToArray(),
            StderrLineCount: _getStderrLineCount(),
            SensitiveValuesSuppressed: true);
    }

    private static JsonElement GetObject(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return default;
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string> GetObjectFieldNames(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()
            : [];
}

internal sealed record ProbeSummary(
    InitializeSummary Initialize,
    AccountSummary AccountRead,
    RateLimitsSummary RateLimitsRead,
    IReadOnlyList<string> NotificationMethodsSeen,
    int StderrLineCount,
    bool SensitiveValuesSuppressed);

internal sealed record InitializeSummary(
    bool Succeeded,
    string? PlatformFamily,
    string? PlatformOs,
    string? UserAgent,
    IReadOnlyList<string> ResponseFields);

internal sealed record AccountSummary(
    bool Succeeded,
    bool AccountPresent,
    string? AccountType,
    IReadOnlyList<string> ResponseFields);

internal sealed record RateLimitsSummary(
    bool Succeeded,
    IReadOnlyList<string> ResponseFields,
    IReadOnlyList<string> LegacySnapshotFields,
    bool PrimaryPresent,
    IReadOnlyList<string> PrimaryFields,
    bool SecondaryPresent,
    IReadOnlyList<string> SecondaryFields,
    bool NamedLimitMapPresent,
    int NamedLimitCount,
    int NormalizedBucketCount,
    string NormalizationState);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProbeSummary))]
internal partial class ProbeJsonContext : JsonSerializerContext;
