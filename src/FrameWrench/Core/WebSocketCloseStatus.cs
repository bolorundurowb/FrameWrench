namespace FrameWrench.Core;

/// <summary>
/// Registered and internal WebSocket close status codes (RFC 6455 §7.4.1).
/// </summary>
/// <remarks>
/// Codes <see cref="NoStatusReceived"/> (1005), <see cref="AbnormalClosure"/> (1006), and
/// <see cref="TlsHandshakeFailure"/> (1015) are pseudo-codes used only in APIs; they must
/// never appear in a Close frame payload on the wire. Application-defined codes use the
/// range 4000–4999.
/// </remarks>
public enum WebSocketCloseStatus : ushort
{
    /// <summary>1000 — Normal closure; the connection completed its purpose.</summary>
    NormalClosure = 1000,

    /// <summary>1001 — Endpoint is going away (shutdown or navigation).</summary>
    GoingAway = 1001,

    /// <summary>1002 — Protocol error; endpoint detected a violation.</summary>
    ProtocolError = 1002,

    /// <summary>1003 — Unsupported data type; endpoint cannot accept the message.</summary>
    UnsupportedData = 1003,

    /// <summary>1005 — No status received (internal pseudo-code; never sent on the wire).</summary>
    NoStatusReceived = 1005,

    /// <summary>1006 — Abnormal closure (internal pseudo-code; never sent on the wire).</summary>
    AbnormalClosure = 1006,

    /// <summary>1007 — Invalid frame payload (for example non-UTF-8 Text data).</summary>
    InvalidPayloadData = 1007,

    /// <summary>1008 — Policy violation; message broke endpoint policy.</summary>
    PolicyViolation = 1008,

    /// <summary>1009 — Message too large for the endpoint to process.</summary>
    MessageTooBig = 1009,

    /// <summary>1010 — Mandatory extension was required but not negotiated.</summary>
    MandatoryExtension = 1010,

    /// <summary>1011 — Unexpected server error.</summary>
    InternalServerError = 1011,

    /// <summary>1015 — TLS handshake failure (internal pseudo-code; never sent on the wire).</summary>
    TlsHandshakeFailure = 1015,
}
