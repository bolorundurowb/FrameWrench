namespace FrameWrench.Internal;

/// <summary>Validates HTTP header names and values for the handshake request.</summary>
internal static class HttpHeaderValidator
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Upgrade",
        "Connection",
        "Sec-WebSocket-Key",
        "Sec-WebSocket-Version",
        "Sec-WebSocket-Protocol",
        "Sec-WebSocket-Extensions",
        "Sec-WebSocket-Accept",
    };

    public static void ValidateExtraHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (var kv in headers)
        {
            ValidateToken(kv.Key, isName: true);
            ValidateToken(kv.Value, isName: false);

            if (ReservedHeaders.Contains(kv.Key))
                throw FrameWrenchErrors.ReservedHeaderOverride(kv.Key);
        }
    }

    private static void ValidateToken(string value, bool isName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw FrameWrenchErrors.HeaderInjection(
                isName ? "(empty name)" : "(empty value)",
                isName ? "Header name cannot be empty." : "Header value cannot be empty.");

        foreach (var ch in value)
        {
            if (ch is '\r' or '\n' or '\0')
                throw FrameWrenchErrors.HeaderInjection(
                    value,
                    "Header names and values must not contain CR, LF, or NUL characters.");
        }
    }
}
