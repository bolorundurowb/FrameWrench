namespace FrameWrench.Core;

/// <summary>
/// Base exception for all FrameWrench errors.
/// </summary>
public class FrameWrenchException : Exception
{
    /// <inheritdoc/>
    public FrameWrenchException() { }

    /// <inheritdoc/>
    public FrameWrenchException(string message) : base(message) { }

    /// <inheritdoc/>
    public FrameWrenchException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Raised when the HTTP Upgrade handshake fails (non-101 status, missing headers,
/// or an invalid <c>Sec-WebSocket-Accept</c> value).
/// </summary>
public class WebSocketHandshakeException : FrameWrenchException
{
    /// <summary>The HTTP status line received from the server, if available.</summary>
    public string? StatusLine { get; }

    /// <inheritdoc/>
    public WebSocketHandshakeException(string message) : base(message) { }

    /// <summary>
    /// Initialises the exception with additional HTTP status-line context.
    /// </summary>
    public WebSocketHandshakeException(string message, string? statusLine)
        : base(message)
    {
        StatusLine = statusLine;
    }

    /// <inheritdoc/>
    public WebSocketHandshakeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Raised when the peer sends a Close frame and the application is reading logical
/// messages via <see cref="FrameWrench.FrameWrenchClient.ReceiveMessageAsync"/>.
/// </summary>
/// <remarks>
/// Close frames are still published to the frame channel for <see cref="FrameWrench.FrameWrenchClient.ReceiveFrameAsync"/>
/// and <see cref="FrameWrench.FrameWrenchClient.GetFrameStream"/>; this exception exists so message-level callers
/// receive the status code and reason instead of skipping control frames silently.
/// </remarks>
public class WebSocketClosedByPeerException : FrameWrenchException
{
    /// <summary>The close status from the peer's Close frame payload, if present.</summary>
    public WebSocketCloseStatus? CloseStatus { get; }

    /// <summary>The UTF-8 reason phrase from the peer, or empty when absent.</summary>
    public string CloseReason { get; }

    /// <summary>Initialises the exception with decoded Close payload fields.</summary>
    public WebSocketClosedByPeerException(WebSocketCloseStatus? closeStatus, string closeReason)
        : base(FormatMessage(closeStatus, closeReason))
    {
        CloseStatus = closeStatus;
        CloseReason = closeReason;
    }

    private static string FormatMessage(WebSocketCloseStatus? closeStatus, string closeReason)
    {
        var statusPart = closeStatus is null
            ? "no status code was supplied"
            : $"status {closeStatus} ({(ushort)closeStatus.Value})";

        if (string.IsNullOrEmpty(closeReason))
            return $"The server closed the WebSocket connection ({statusPart}).";

        return $"The server closed the WebSocket connection ({statusPart}): {closeReason}";
    }
}

/// <summary>
/// Raised when an RFC 6455 protocol rule is violated during frame parsing or sending
/// (e.g., a reserved opcode, an unmasked server frame, a fragmented control frame,
/// or a control frame with a payload &gt; 125 bytes).
/// </summary>
public class WebSocketProtocolException : FrameWrenchException
{
    /// <inheritdoc/>
    public WebSocketProtocolException(string message) : base(message) { }

    /// <inheritdoc/>
    public WebSocketProtocolException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Raised when an operation is attempted on a <see cref="FrameWrenchClient"/>
/// that is not in the required state (e.g., sending after close).
/// </summary>
public class WebSocketStateException : FrameWrenchException
{
    /// <summary>The state the client was in when the exception was thrown.</summary>
    public WebSocketState CurrentState { get; }

    /// <summary>Initialises the exception with state context.</summary>
    public WebSocketStateException(WebSocketState state, string message)
        : base(message)
    {
        CurrentState = state;
    }
}
