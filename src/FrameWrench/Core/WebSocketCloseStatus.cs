namespace FrameWrench.Core;

/// <summary>
/// Well-known WebSocket close status codes defined in RFC 6455 §7.4.1.
/// </summary>
/// <remarks>
/// Status codes 1005, 1006, and 1015 are pseudo-codes used only internally;
/// they must never appear in an actual Close frame payload.
/// Application-defined codes may be in the range 4000–4999.
/// </remarks>
public enum WebSocketCloseStatus : ushort
{
    /// <summary>1000 – Normal closure; the connection successfully completed its purpose.</summary>
    NormalClosure = 1000,

    /// <summary>1001 – Going Away; the endpoint is going away (server shutdown, browser navigation).</summary>
    GoingAway = 1001,

    /// <summary>1002 – Protocol Error; the endpoint terminated due to a protocol violation.</summary>
    ProtocolError = 1002,

    /// <summary>1003 – Unsupported Data; the endpoint cannot accept the received data type.</summary>
    UnsupportedData = 1003,

    /// <summary>1005 – No Status Received (internal / pseudo-code, never sent on the wire).</summary>
    NoStatusReceived = 1005,

    /// <summary>1006 – Abnormal Closure (internal / pseudo-code, never sent on the wire).</summary>
    AbnormalClosure = 1006,

    /// <summary>1007 – Invalid Frame Payload Data; e.g., non-UTF-8 text payload.</summary>
    InvalidPayloadData = 1007,

    /// <summary>1008 – Policy Violation; the message violates the endpoint's policy.</summary>
    PolicyViolation = 1008,

    /// <summary>1009 – Message Too Big; the message exceeds the endpoint's processing limit.</summary>
    MessageTooBig = 1009,

    /// <summary>1010 – Mandatory Extension; the client requires an extension the server did not negotiate.</summary>
    MandatoryExtension = 1010,

    /// <summary>1011 – Internal Server Error; the server encountered an unexpected condition.</summary>
    InternalServerError = 1011,

    /// <summary>1015 – TLS Handshake Failure (internal / pseudo-code, never sent on the wire).</summary>
    TlsHandshakeFailure = 1015,
}
