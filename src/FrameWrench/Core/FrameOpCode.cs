namespace FrameWrench.Core;

/// <summary>
/// WebSocket frame opcodes (RFC 6455 §5.2).
/// </summary>
/// <remarks>
/// Values <c>0x3</c>–<c>0x7</c> are reserved for future non-control frames.
/// Values <c>0xB</c>–<c>0xF</c> are reserved for future control frames.
/// An endpoint that receives an unnegotiated reserved opcode must fail the connection.
/// </remarks>
public enum FrameOpCode : byte
{
    /// <summary>Continuation (0x0): payload continues the current fragmented message.</summary>
    Continuation = 0x0,

    /// <summary>Text (0x1): payload is UTF-8 encoded application text.</summary>
    Text = 0x1,

    /// <summary>Binary (0x2): payload is opaque application data.</summary>
    Binary = 0x2,

    /// <summary>Close (0x8): begins or acknowledges the closing handshake.</summary>
    Close = 0x8,

    /// <summary>
    /// Ping (0x9): keep-alive or latency probe; the peer should respond with
    /// <see cref="Pong"/> carrying the same application data.
    /// </summary>
    Ping = 0x9,

    /// <summary>
    /// Pong (0xA): response to a <see cref="Ping"/> or an unsolicited heartbeat.
    /// </summary>
    Pong = 0xA,
}
