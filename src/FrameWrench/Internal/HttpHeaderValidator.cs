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
            ValidateToken(kv.Key, kv.Key, isName: true);
            ValidateToken(kv.Key, kv.Value, isName: false);

            if (ReservedHeaders.Contains(kv.Key))
                throw FrameWrenchErrors.ReservedHeaderOverride(kv.Key);
        }
    }

    private static void ValidateToken(string headerName, string token, bool isName)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw FrameWrenchErrors.InvalidHeader(
                isName ? "(empty name)" : headerName,
                isName,
                isName ? "Header name cannot be empty." : "Header value cannot be empty.");

        foreach (var ch in token)
        {
            if (ch is '\r' or '\n' or '\0')
                throw FrameWrenchErrors.InvalidHeader(
                    headerName,
                    isName,
                    "Header names and values must not contain CR, LF, or NUL characters.");
        }
    }
}
