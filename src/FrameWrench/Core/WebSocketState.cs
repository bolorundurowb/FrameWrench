namespace FrameWrench.Core;

/// <summary>
/// Represents the connection-lifecycle state of a <see cref="FrameWrenchClient"/>
/// following the state machine described in RFC 6455 §4 and §7.
/// </summary>
public enum WebSocketState
{
    /// <summary>The client has been created but <c>ConnectAsync</c> has not yet been called.</summary>
    None = 0,

    /// <summary>The TCP connection and HTTP Upgrade handshake are in progress.</summary>
    Connecting = 1,

    /// <summary>The WebSocket connection is fully established; frames may be exchanged.</summary>
    Open = 2,

    /// <summary>
    /// A Close frame has been sent by the local endpoint; waiting for the peer's echoing Close frame
    /// to complete the closing handshake (RFC 6455 §7.1.2).
    /// </summary>
    CloseSent = 3,

    /// <summary>
    /// A Close frame has been received from the peer; the local endpoint must reply with a Close echo.
    /// FrameWrench sends the echo automatically in the receive pump.
    /// </summary>
    CloseReceived = 4,

    /// <summary>The closing handshake has completed; the TCP connection is closed.</summary>
    Closed = 5,

    /// <summary>
    /// The connection was terminated abnormally (I/O error, protocol violation) without completing
    /// the closing handshake.
    /// </summary>
    Aborted = 6,
}
