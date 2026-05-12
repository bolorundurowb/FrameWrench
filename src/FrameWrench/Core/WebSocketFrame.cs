using System.Text;
using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// Represents a single, fully-parsed WebSocket frame (RFC 6455 §5).
/// Payload bytes are always in their unmasked form.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fragmentation:</strong> The first fragment of a multi-frame message carries
/// the data opcode (<see cref="FrameOpCode.Text"/> or <see cref="FrameOpCode.Binary"/>)
/// with <see cref="IsFinal"/> = <c>false</c>.  Subsequent fragments use
/// <see cref="FrameOpCode.Continuation"/>, and the last fragment sets
/// <see cref="IsFinal"/> = <c>true</c>.
/// </para>
/// <para>
/// <strong>Control frames</strong> (Close, Ping, Pong) must always be final and their
/// payload is limited to 125 bytes (RFC 6455 §5.5).
/// </para>
/// <para>Instances are immutable; use the static factory methods for convenient construction.</para>
/// </remarks>
public sealed class WebSocketFrame
{
    /// <summary>
    /// Initialises a new <see cref="WebSocketFrame"/>.
    /// </summary>
    /// <param name="opCode">The frame opcode.</param>
    /// <param name="isFinal">Whether the FIN bit is set (last or only fragment).</param>
    /// <param name="payload">Unmasked payload bytes.</param>
    /// <param name="rsv1">RSV1 extension bit (default <c>false</c>).</param>
    /// <param name="rsv2">RSV2 extension bit (default <c>false</c>).</param>
    /// <param name="rsv3">RSV3 extension bit (default <c>false</c>).</param>
    public WebSocketFrame(
        FrameOpCode opCode,
        bool isFinal,
        ReadOnlyMemory<byte> payload,
        bool rsv1 = false,
        bool rsv2 = false,
        bool rsv3 = false)
    {
        OpCode = opCode;
        IsFinal = isFinal;
        Payload = payload;
        Rsv1 = rsv1;
        Rsv2 = rsv2;
        Rsv3 = rsv3;
    }

    /// <summary>The opcode that classifies this frame.</summary>
    public FrameOpCode OpCode { get; }

    /// <summary>
    /// <c>true</c> when the FIN bit is set - this is the final (or only) fragment
    /// of the message.
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>Unmasked payload data. Empty for ping/pong/close frames with no body.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>RSV1 extension bit (e.g., used by per-message deflate).</summary>
    public bool Rsv1 { get; }

    /// <summary>RSV2 extension bit.</summary>
    public bool Rsv2 { get; }

    /// <summary>RSV3 extension bit.</summary>
    public bool Rsv3 { get; }

    /// <summary>
    /// Returns <c>true</c> for control frames (Close, Ping, Pong).
    /// Control frames must not be fragmented (RFC 6455 §5.5).
    /// </summary>
    public bool IsControl =>
        OpCode is FrameOpCode.Close or FrameOpCode.Ping or FrameOpCode.Pong;

    /// <summary>
    /// Decodes the payload as a UTF-8 string. Intended for <see cref="FrameOpCode.Text"/> frames.
    /// </summary>
    public string GetTextPayload() =>
        Utf8StringUtil.GetString(Payload);

    /// <summary>
    /// For a <see cref="FrameOpCode.Close"/> frame, extracts the optional status code
    /// and reason phrase from the payload (RFC 6455 §5.5.1).
    /// </summary>
    /// <param name="statusCode">
    /// The big-endian 16-bit status code, or <c>null</c> when the Close payload is empty.
    /// </param>
    /// <param name="reason">
    /// The UTF-8 reason phrase following the status code, or <see cref="string.Empty"/> when absent.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called on a non-Close frame.
    /// </exception>
    public void GetCloseData(out WebSocketCloseStatus? statusCode, out string reason)
    {
        if (OpCode != FrameOpCode.Close)
            throw new InvalidOperationException(
                $"GetCloseData may only be invoked on Close frames (this frame's opcode is {OpCode}).");

        var span = Payload.Span;
        if (span.Length < 2)
        {
            statusCode = null;
            reason = string.Empty;
            return;
        }

        statusCode = (WebSocketCloseStatus)((span[0] << 8) | span[1]);
        reason = span.Length > 2
            ? Utf8StringUtil.GetString(Payload.Slice(2))
            : string.Empty;
    }

    /// <summary>
    /// Creates a final Text frame with a UTF-8 encoded payload.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="isFinal">
    /// Pass <c>false</c> to begin a fragmented text message; the first fragment uses
    /// the Text opcode and subsequent fragments use
    /// <see cref="Continuation(ReadOnlyMemory{byte}, bool)"/>.
    /// </param>
    public static WebSocketFrame Text(string text, bool isFinal = true) =>
        new(FrameOpCode.Text, isFinal, Encoding.UTF8.GetBytes(text));

    /// <summary>Creates a Binary frame.</summary>
    /// <param name="data">The binary payload.</param>
    /// <param name="isFinal">Pass <c>false</c> to start a fragmented binary message.</param>
    public static WebSocketFrame Binary(ReadOnlyMemory<byte> data, bool isFinal = true) =>
        new(FrameOpCode.Binary, isFinal, data);

    /// <summary>
    /// Creates a Continuation frame for use within a fragmented message.
    /// </summary>
    /// <param name="data">The fragment payload.</param>
    /// <param name="isFinal">
    /// <c>true</c> for the last fragment; <c>false</c> for intermediate fragments.
    /// </param>
    public static WebSocketFrame Continuation(ReadOnlyMemory<byte> data, bool isFinal) =>
        new(FrameOpCode.Continuation, isFinal, data);

    /// <summary>
    /// Creates a Ping frame. The payload (≤ 125 bytes) is echoed back verbatim in the Pong.
    /// </summary>
    /// <param name="payload">Optional arbitrary payload used to correlate the Pong.</param>
    public static WebSocketFrame Ping(ReadOnlyMemory<byte> payload = default) =>
        new(FrameOpCode.Ping, isFinal: true, payload);

    /// <summary>Creates a Pong frame.</summary>
    /// <param name="payload">
    /// Should mirror the payload of the corresponding Ping.
    /// May also be sent unsolicited as a unidirectional heartbeat.
    /// </param>
    public static WebSocketFrame Pong(ReadOnlyMemory<byte> payload = default) =>
        new(FrameOpCode.Pong, isFinal: true, payload);

    /// <summary>
    /// Creates a Close frame with the given status code and optional reason phrase.
    /// </summary>
    /// <param name="status">
    /// The close status code (default <see cref="WebSocketCloseStatus.NormalClosure"/>).
    /// </param>
    /// <param name="reason">
    /// A UTF-8 reason phrase.  Its encoded form must not exceed 123 bytes
    /// (125 byte payload limit minus the 2-byte status code).
    /// </param>
    public static WebSocketFrame Close(
        WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
        string? reason = null)
    {
        var reasonBytes = reason is { Length: > 0 }
            ? Encoding.UTF8.GetBytes(reason)
            : [];

        var payload = new byte[2 + reasonBytes.Length];
        var code = (ushort)status;
        payload[0] = (byte)(code >> 8);
        payload[1] = (byte)(code & 0xFF);
        if (reasonBytes.Length > 0)
            reasonBytes.CopyTo(payload, 2);

        return new WebSocketFrame(FrameOpCode.Close, isFinal: true, payload);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"FrameWrench.WebSocketFrame [{OpCode}{(IsFinal ? "" : " fragment")} len={Payload.Length}]";
}
