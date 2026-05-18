namespace FrameWrench.Core;

/// <summary>
/// Result of <see cref="FrameWrench.FrameWrenchClient.CloseAsync"/>.
/// </summary>
public readonly struct CloseResult
{
    /// <summary>Initialises a close result.</summary>
    public CloseResult(bool handshakeCompleted, WebSocketState finalState)
    {
        HandshakeCompleted = handshakeCompleted;
        FinalState = finalState;
    }

    /// <summary>
    /// <c>true</c> when the peer's Close echo was received within
    /// <see cref="FrameWrench.FrameWrenchOptions.CloseHandshakeTimeout"/>.
    /// </summary>
    public bool HandshakeCompleted { get; }

    /// <summary>Connection state after <see cref="FrameWrench.FrameWrenchClient.CloseAsync"/> returns.</summary>
    public WebSocketState FinalState { get; }
}
