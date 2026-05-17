using System.Text;
using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// Validates Close frame status codes and reason phrases per RFC 6455 §7.4.1.
/// </summary>
public static class CloseFrameValidator
{
    /// <summary>Maximum UTF-8 bytes for the reason after the 2-byte status code.</summary>
    public const int MaxReasonUtf8Bytes = 123;

    /// <summary>Maximum total Close payload including status code.</summary>
    public const int MaxClosePayloadBytes = 125;

    /// <summary>
    /// Parses a Close frame payload into <see cref="CloseFrameInfo"/> without validating UTF-8.
    /// </summary>
    public static CloseFrameInfo Parse(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < 2)
            return new CloseFrameInfo(null, null, string.Empty);

        var code = (ushort)((span[0] << 8) | span[1]);
        var status = TryMapWireStatus(code);
        var reason = span.Length > 2
            ? Encoding.UTF8.GetString(span.Slice(2).ToArray())
            : string.Empty;

        return new CloseFrameInfo(code, status, reason);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="statusCode"/> may appear on the wire.
    /// </summary>
    public static bool IsValidWireStatus(ushort statusCode)
    {
        if (statusCode is < 1000 or 1004 or 1005 or 1006 or 1015)
            return false;

        if (statusCode is 1012 or 1013 or 1014)
            return false;

        if (statusCode is >= 1000 and <= 1011)
            return true;

        return statusCode is >= 3000 and <= 4999;
    }

    /// <summary>Maps a wire code to <see cref="WireCloseStatus"/> when registered.</summary>
    public static WireCloseStatus? TryMapWireStatus(ushort statusCode) =>
        TryMapKnownStatus(statusCode);

    private static WireCloseStatus? TryMapKnownStatus(ushort statusCode) =>
        statusCode switch
        {
            1000 => WireCloseStatus.NormalClosure,
            1001 => WireCloseStatus.GoingAway,
            1002 => WireCloseStatus.ProtocolError,
            1003 => WireCloseStatus.UnsupportedData,
            1007 => WireCloseStatus.InvalidPayloadData,
            1008 => WireCloseStatus.PolicyViolation,
            1009 => WireCloseStatus.MessageTooBig,
            1010 => WireCloseStatus.MandatoryExtension,
            1011 => WireCloseStatus.InternalServerError,
            _ => null,
        };

    /// <summary>
    /// Validates outbound close parameters; throws <see cref="WebSocketProtocolException"/> on failure.
    /// </summary>
    public static void ValidateForSend(WireCloseStatus status, string? reason) =>
        ValidateForSend((ushort)status, reason);

    /// <summary>
    /// Validates outbound close parameters for any wire-valid status code (including 3000–4999).
    /// </summary>
    public static void ValidateForSend(ushort statusCode, string? reason)
    {
        if (!IsValidWireStatus(statusCode))
            throw FrameWrenchErrors.InvalidCloseStatus(statusCode, inbound: false);

        if (string.IsNullOrEmpty(reason))
            return;

        var reasonBytes = Encoding.UTF8.GetByteCount(reason);
        if (reasonBytes > MaxReasonUtf8Bytes)
            throw FrameWrenchErrors.CloseReasonTooLong(reasonBytes);
    }

    /// <summary>
    /// Validates inbound Close payload; throws <see cref="WebSocketProtocolException"/> on failure.
    /// </summary>
    public static void ThrowIfInvalidOnWire(ReadOnlyMemory<byte> payload, bool validateUtf8)
    {
        if (payload.Length > MaxClosePayloadBytes)
            throw FrameWrenchErrors.ControlPayloadTooLarge(payload.Length);

        if (payload.Length >= 2)
        {
            var span = payload.Span;
            var code = (ushort)((span[0] << 8) | span[1]);
            if (!IsValidWireStatus(code))
                throw FrameWrenchErrors.InvalidCloseStatus(code, inbound: true);
        }

        if (validateUtf8)
            Utf8Validator.ThrowIfInvalidCloseReason(payload);
    }
}
