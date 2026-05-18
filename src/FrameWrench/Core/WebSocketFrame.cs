using System.Text;
using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// A single parsed WebSocket frame (RFC 6455 §5) with an unmasked payload.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fragmentation:</strong> The first fragment of a multi-frame message uses
/// <see cref="FrameOpCode.Text"/> or <see cref="FrameOpCode.Binary"/> with
/// <see cref="IsFinal"/> <c>false</c>. Later fragments use
/// <see cref="FrameOpCode.Continuation"/>; the last fragment sets <see cref="IsFinal"/>
/// <c>true</c>.
/// </para>
/// <para>
/// <strong>Control frames</strong> (<see cref="FrameOpCode.Close"/>,
/// <see cref="FrameOpCode.Ping"/>, <see cref="FrameOpCode.Pong"/>) must be final and have
/// at most 125 bytes of payload (RFC 6455 §5.5).
/// </para>
/// <para>Instances are immutable. Prefer the static factory methods for construction.</para>
/// </remarks>
public sealed class WebSocketFrame
{
    /// <summary>Initialises a new <see cref="WebSocketFrame"/>.</summary>
    /// <param name="opCode">The frame opcode.</param>
    /// <param name="isFinal"><c>true</c> when the FIN bit is set (only or last fragment).</param>
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

    /// <summary>Gets the opcode that classifies this frame.</summary>
    public FrameOpCode OpCode { get; }

    /// <summary>
    /// Gets whether the FIN bit is set, indicating this is the only or final fragment of a message.
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>
    /// Gets the unmasked payload bytes. May be empty for control frames with no body.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Gets the RSV1 bit (for example per-message deflate when negotiated).
    /// </summary>
    public bool Rsv1 { get; }

    /// <summary>Gets the RSV2 bit.</summary>
    public bool Rsv2 { get; }

    /// <summary>Gets the RSV3 bit.</summary>
    public bool Rsv3 { get; }

    /// <summary>
    /// Gets whether this frame is a control frame (<see cref="FrameOpCode.Close"/>,
    /// <see cref="FrameOpCode.Ping"/>, or <see cref="FrameOpCode.Pong"/>).
    /// Control frames must not be fragmented (RFC 6455 §5.5).
    /// </summary>
    public bool IsControl =>
        OpCode is FrameOpCode.Close or FrameOpCode.Ping or FrameOpCode.Pong;

    /// <summary>
    /// Decodes <see cref="Payload"/> as a UTF-8 string.
    /// </summary>
    /// <returns>The text content of the frame.</returns>
    /// <remarks>Intended for <see cref="FrameOpCode.Text"/> frames.</remarks>
    public string GetTextPayload() =>
        Utf8StringUtil.GetString(Payload);

    /// <summary>
    /// Parses Close frame payload into <see cref="CloseFrameInfo"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="OpCode"/> is not <see cref="FrameOpCode.Close"/>.
    /// </exception>
    public CloseFrameInfo GetCloseInfo()
    {
        if (OpCode != FrameOpCode.Close)
            throw new InvalidOperationException(
                $"GetCloseInfo may only be invoked on Close frames (this frame's opcode is {OpCode}).");

        return CloseFrameValidator.Parse(Payload);
    }

    /// <summary>Creates a Text frame with a UTF-8 encoded payload.</summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="isFinal">
    /// <c>false</c> to start a fragmented text message; follow with
    /// <see cref="Continuation"/> fragments.
    /// </param>
    /// <returns>A new Text <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Text(string text, bool isFinal = true) =>
        new(FrameOpCode.Text, isFinal, Encoding.UTF8.GetBytes(text));

    /// <summary>Creates a Binary frame.</summary>
    /// <param name="data">The binary payload.</param>
    /// <param name="isFinal">
    /// <c>false</c> to start a fragmented binary message; follow with
    /// <see cref="Continuation"/> fragments.
    /// </param>
    /// <returns>A new Binary <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Binary(ReadOnlyMemory<byte> data, bool isFinal = true) =>
        new(FrameOpCode.Binary, isFinal, data);

    /// <summary>Creates a Continuation frame within a fragmented message.</summary>
    /// <param name="data">The fragment payload.</param>
    /// <param name="isFinal"><c>true</c> for the last fragment; <c>false</c> otherwise.</param>
    /// <returns>A new Continuation <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Continuation(ReadOnlyMemory<byte> data, bool isFinal) =>
        new(FrameOpCode.Continuation, isFinal, data);

    /// <summary>Creates a Ping frame.</summary>
    /// <param name="payload">
    /// Optional application data (at most 125 bytes) echoed in the corresponding Pong.
    /// </param>
    /// <returns>A new Ping <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Ping(ReadOnlyMemory<byte> payload = default) =>
        new(FrameOpCode.Ping, isFinal: true, payload);

    /// <summary>Creates a Pong frame.</summary>
    /// <param name="payload">
    /// Application data mirroring the corresponding Ping, or arbitrary data for an
    /// unsolicited heartbeat (RFC 6455 §5.5.3–5.5.4).
    /// </param>
    /// <returns>A new Pong <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Pong(ReadOnlyMemory<byte> payload = default) =>
        new(FrameOpCode.Pong, isFinal: true, payload);

    /// <summary>Creates a Close frame.</summary>
    /// <param name="status">The close status code.</param>
    /// <param name="reason">
    /// Optional UTF-8 reason phrase. The encoded form must not exceed 123 bytes
    /// (125-byte payload limit minus the 2-byte status code).
    /// </param>
    /// <returns>A new Close <see cref="WebSocketFrame"/>.</returns>
    public static WebSocketFrame Close(
        WireCloseStatus status = WireCloseStatus.NormalClosure,
        string? reason = null) =>
        Close((ushort)status, reason);

    /// <summary>
    /// Creates a Close frame with any wire-valid status code (including application-defined 3000–4999).
    /// </summary>
    public static WebSocketFrame Close(ushort statusCode, string? reason = null)
    {
        CloseFrameValidator.ValidateForSend(statusCode, reason);

        var reasonBytes = reason is { Length: > 0 }
            ? Encoding.UTF8.GetBytes(reason)
            : [];

        var payload = new byte[2 + reasonBytes.Length];
        payload[0] = (byte)(statusCode >> 8);
        payload[1] = (byte)(statusCode & 0xFF);
        if (reasonBytes.Length > 0)
            reasonBytes.CopyTo(payload, 2);

        return new WebSocketFrame(FrameOpCode.Close, isFinal: true, payload);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"FrameWrench.WebSocketFrame [{OpCode}{(IsFinal ? "" : " fragment")} len={Payload.Length}]";
}
