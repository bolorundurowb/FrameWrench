namespace FrameWrench.Core;

/// <summary>
/// WebSocket frame opcodes as defined in RFC 6455 §5.2.
/// </summary>
/// <remarks>
/// Values 0x3-0x7 are reserved for future non-control frames.
/// Values 0xB-0xF are reserved for future control frames.
/// Any received frame with a reserved opcode that has not been negotiated as
/// an extension causes a protocol-error close.
/// </remarks>
public enum FrameOpCode : byte
{
    /// <summary>Continuation frame (0x0) - carries a fragment of the current message.</summary>
    Continuation = 0x0,

    /// <summary>Text frame (0x1) - payload is UTF-8 encoded text.</summary>
    Text = 0x1,

    /// <summary>Binary frame (0x2) - payload is arbitrary binary data.</summary>
    Binary = 0x2,

    /// <summary>Close frame (0x8) - initiates or acknowledges the closing handshake.</summary>
    Close = 0x8,

    /// <summary>Ping frame (0x9) - keep-alive or latency probe. Must be answered with a Pong.</summary>
    Ping = 0x9,

    /// <summary>Pong frame (0xA) - unsolicited or in response to a Ping.</summary>
    Pong = 0xA,
}
