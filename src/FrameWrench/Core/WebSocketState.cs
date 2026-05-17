namespace FrameWrench.Core;

/// <summary>
/// Connection lifecycle state of a <see cref="FrameWrench.FrameWrenchClient"/>,
/// aligned with RFC 6455 §4 (opening) and §7 (closing).
/// </summary>
public enum WebSocketState
{
    /// <summary>
    /// The client was created but
    /// <see cref="FrameWrench.FrameWrenchClient.ConnectAsync(System.Uri, System.Threading.CancellationToken)"/>
    /// has not completed successfully.
    /// </summary>
    None = 0,

    /// <summary>
    /// TCP connection and HTTP Upgrade handshake are in progress.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// The WebSocket is open; application data and control frames may be exchanged.
    /// </summary>
    Open = 2,

    /// <summary>
    /// A Close frame was sent locally; awaiting the peer's Close echo (RFC 6455 §7.1.2).
    /// </summary>
    CloseSent = 3,

    /// <summary>
    /// A Close frame was received from the peer; the receive pump sends a Close echo automatically.
    /// </summary>
    CloseReceived = 4,

    /// <summary>
    /// The closing handshake completed and the transport is closed.
    /// </summary>
    Closed = 5,

    /// <summary>
    /// The connection ended abnormally (I/O failure, protocol violation, or incomplete close)
    /// without a clean closing handshake.
    /// </summary>
    Aborted = 6,
}
