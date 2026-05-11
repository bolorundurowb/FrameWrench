using System.Text;
using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>
/// Strict UTF-8 validation for RFC 6455 Text message payloads and Close reason phrases.
/// </summary>
internal static class Utf8Validator
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Throws <see cref="WebSocketProtocolException"/> if <paramref name="utf8Bytes"/> is not well-formed UTF-8.
    /// </summary>
    public static void ThrowIfInvalidUtf8(ReadOnlySpan<byte> utf8Bytes) =>
        ThrowIfInvalidUtf8(utf8Bytes, "Invalid UTF-8 in a Text message. RFC 6455 §8.1 requires failing the WebSocket connection.");

    private static void ThrowIfInvalidUtf8(ReadOnlySpan<byte> utf8Bytes, string message)
    {
        if (utf8Bytes.IsEmpty)
            return;

        try
        {
            _ = StrictUtf8.GetString(utf8Bytes.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        {
            throw new WebSocketProtocolException(message, ex);
        }
    }

    /// <summary>
    /// Validates the optional UTF-8 reason in a Close frame payload (bytes after the 2-byte status code).
    /// </summary>
    public static void ThrowIfInvalidCloseReason(ReadOnlyMemory<byte> closePayload)
    {
        if (closePayload.Length <= 2)
            return;

        ThrowIfInvalidUtf8(
            closePayload.Span.Slice(2),
            "Invalid UTF-8 in a Close frame reason. RFC 6455 §7.4.1 requires failing the WebSocket connection.");
    }
}
