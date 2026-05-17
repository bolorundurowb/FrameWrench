namespace FrameWrench.Core;

/// <summary>
/// WebSocket close status codes that may appear in a Close frame on the wire (RFC 6455 §7.4.1).
/// </summary>
/// <remarks>
/// Pseudo-codes such as 1005, 1006, and 1015 are not included; use
/// <see cref="WebSocketCloseStatus"/> for local-only states.
/// </remarks>
public enum WireCloseStatus : ushort
{
    /// <summary>1000 — Normal closure.</summary>
    NormalClosure = 1000,

    /// <summary>1001 — Endpoint is going away.</summary>
    GoingAway = 1001,

    /// <summary>1002 — Protocol error.</summary>
    ProtocolError = 1002,

    /// <summary>1003 — Unsupported data.</summary>
    UnsupportedData = 1003,

    /// <summary>1007 — Invalid frame payload data.</summary>
    InvalidPayloadData = 1007,

    /// <summary>1008 — Policy violation.</summary>
    PolicyViolation = 1008,

    /// <summary>1009 — Message too big.</summary>
    MessageTooBig = 1009,

    /// <summary>1010 — Mandatory extension required.</summary>
    MandatoryExtension = 1010,

    /// <summary>1011 — Internal server error.</summary>
    InternalServerError = 1011,
}
