using System.Text.RegularExpressions;

namespace CodexLimitMonitor.App.Services;

internal static partial class DiagnosticSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = EmailPattern().Replace(value, "[redacted-email]");
        sanitized = CredentialPattern().Replace(sanitized, "$1 [redacted-secret]");
        sanitized = JwtPattern().Replace(sanitized, "[redacted-token]");
        return LongSecretPattern().Replace(sanitized, "[redacted-secret]");
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(authorization|bearer|access[_-]?token|refresh[_-]?token|cookie)\s*[:=]?\s*(?:Bearer\s+)?\S+", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}(?:\.[A-Za-z0-9_-]{8,})?\b")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"\b[A-Za-z0-9_-]{40,}\b")]
    private static partial Regex LongSecretPattern();
}
