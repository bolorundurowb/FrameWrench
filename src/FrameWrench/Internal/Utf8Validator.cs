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

    public static void ThrowIfInvalidUtf8(ReadOnlySpan<byte> utf8Bytes, bool inbound = true)
    {
        if (utf8Bytes.IsEmpty)
            return;

        try
        {
            _ = StrictUtf8.GetString(utf8Bytes.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        {
            throw FrameWrenchErrors.InvalidUtf8(inbound, "Text message");
        }
    }

    public static void ThrowIfInvalidCloseReason(ReadOnlyMemory<byte> closePayload, bool inbound = true)
    {
        if (closePayload.Length <= 2)
            return;

        try
        {
            _ = StrictUtf8.GetString(closePayload.Span.Slice(2).ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
        {
            throw FrameWrenchErrors.InvalidUtf8(inbound, "Close reason");
        }
    }
}
