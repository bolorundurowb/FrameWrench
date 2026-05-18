namespace FrameWrench.Internal;

/// <summary>Redacts secrets from error detail fields before they are exposed via exceptions.</summary>
internal static class SensitiveDataRedactor
{
    public const string RedactedPlaceholder = "<redacted>";

    private static readonly IReadOnlyDictionary<string, string> EmptyContext =
        new Dictionary<string, string>();

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "Sec-WebSocket-Key",
    };

    private static readonly HashSet<string> AlwaysRedactValueKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "headerValue",
    };

    public static (string Title, string Explanation, IReadOnlyDictionary<string, string> Context) SanitizeDetailFields(
        string title,
        string explanation,
        IReadOnlyDictionary<string, string>? context)
    {
        var sanitizedContext = SanitizeContext(context);
        return (
            SanitizeText(title) ?? string.Empty,
            SanitizeText(explanation) ?? string.Empty,
            sanitizedContext);
    }

    public static IReadOnlyDictionary<string, string> SanitizeContext(
        IReadOnlyDictionary<string, string>? context)
    {
        if (context is null || context.Count == 0)
            return EmptyContext;

        var result = new Dictionary<string, string>(context.Count, StringComparer.Ordinal);
        foreach (var kv in context)
            result[kv.Key] = SanitizeContextValue(kv.Key, kv.Value);
        return result;
    }

    public static string? SanitizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (ContainsBearerCredential(text!))
            return RedactBearerCredentials(text!);

        return text;
    }

    internal static bool IsSensitiveHeaderName(string? name) =>
        !string.IsNullOrEmpty(name) && SensitiveHeaderNames.Contains(name!);

    private static string SanitizeContextValue(string key, string value)
    {
        if (AlwaysRedactValueKeys.Contains(key))
            return RedactedPlaceholder;

        if (string.Equals(key, "headerName", StringComparison.OrdinalIgnoreCase))
            return value;

        if (IsSensitiveHeaderName(key))
            return RedactedPlaceholder;

        if (ContainsBearerCredential(value))
            return RedactedPlaceholder;

        return value;
    }

    private static bool ContainsBearerCredential(string text) =>
        text.IndexOf("Bearer ", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string RedactBearerCredentials(string text)
    {
        var result = text;
        var searchFrom = 0;
        while (true)
        {
            var index = result.IndexOf("Bearer ", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            var credentialStart = index + "Bearer ".Length;
            var credentialEnd = credentialStart;
            while (credentialEnd < result.Length && !char.IsWhiteSpace(result[credentialEnd]))
                credentialEnd++;

            result = result.Substring(0, index) + RedactedPlaceholder + result.Substring(credentialEnd);
            searchFrom = index + RedactedPlaceholder.Length;
        }

        return result;
    }
}
