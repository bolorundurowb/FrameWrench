namespace FrameWrench.Core;

/// <summary>
/// Base exception for all FrameWrench errors.
/// </summary>
/// <remarks>
/// Catch this type to handle any library-specific failure. More specific types such as
/// <see cref="WebSocketHandshakeException"/>, <see cref="WebSocketProtocolException"/>,
/// <see cref="WebSocketStateException"/>, and <see cref="WebSocketClosedByPeerException"/>
/// derive from this class.
/// </remarks>
public class FrameWrenchException : Exception
{
    /// <summary>Initialises a new instance of <see cref="FrameWrenchException"/>.</summary>
    public FrameWrenchException() { }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the error.</param>
    public FrameWrenchException(string message) : base(message) { }

    /// <summary>Initialises a new instance with the specified message and inner exception.</summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="inner">The exception that caused this error.</param>
    public FrameWrenchException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The HTTP Upgrade handshake failed (non-101 status, missing or invalid headers, or an
/// incorrect <c>Sec-WebSocket-Accept</c> value).
/// </summary>
public class WebSocketHandshakeException : FrameWrenchException
{
    /// <summary>
    /// Gets the HTTP status line received from the server, when available.
    /// </summary>
    public string? StatusLine { get; }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the handshake failure.</param>
    public WebSocketHandshakeException(string message) : base(message) { }

    /// <summary>
    /// Initialises a new instance with the specified message and HTTP status line.
    /// </summary>
    /// <param name="message">A description of the handshake failure.</param>
    /// <param name="statusLine">The first line of the HTTP response, if parsed.</param>
    public WebSocketHandshakeException(string message, string? statusLine)
        : base(message)
    {
        StatusLine = statusLine;
    }

    /// <summary>Initialises a new instance with the specified message and inner exception.</summary>
    /// <param name="message">A description of the handshake failure.</param>
    /// <param name="inner">The exception that caused this error.</param>
    public WebSocketHandshakeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The peer sent a Close frame while the application was reading logical messages via
/// <see cref="FrameWrench.FrameWrenchClient.ReceiveMessageAsync(System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// Close frames are still delivered on the frame channel through
/// <see cref="FrameWrench.FrameWrenchClient.ReceiveFrameAsync(System.Threading.CancellationToken)"/>
/// and <see cref="FrameWrench.FrameWrenchClient.GetFrameStream(System.Threading.CancellationToken)"/>.
/// This exception gives message-level callers the status code and reason without silently
/// skipping control frames.
/// </remarks>
public class WebSocketClosedByPeerException : FrameWrenchException
{
    /// <summary>
    /// Gets the close status code from the peer's Close frame payload, or <c>null</c> when
    /// the payload contained no status code.
    /// </summary>
    public WebSocketCloseStatus? CloseStatus { get; }

    /// <summary>
    /// Gets the UTF-8 reason phrase from the peer's Close frame, or
    /// <see cref="string.Empty"/> when absent.
    /// </summary>
    public string CloseReason { get; }

    /// <summary>Initialises a new instance from decoded Close payload fields.</summary>
    /// <param name="closeStatus">The status code, if present in the payload.</param>
    /// <param name="closeReason">The reason phrase, which may be empty.</param>
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
/// An RFC 6455 protocol rule was violated during frame processing or sending.
/// </summary>
/// <remarks>
/// Examples include reserved opcodes, unmasked server frames, fragmented control frames,
/// control payloads larger than 125 bytes, invalid UTF-8 in Text or Close payloads (when
/// validation is enabled), and §5.4 fragmentation ordering errors.
/// </remarks>
public class WebSocketProtocolException : FrameWrenchException
{
    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the protocol violation.</param>
    public WebSocketProtocolException(string message) : base(message) { }

    /// <summary>Initialises a new instance with the specified message and inner exception.</summary>
    /// <param name="message">A description of the protocol violation.</param>
    /// <param name="inner">The exception that caused this error.</param>
    public WebSocketProtocolException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// An operation was attempted on a <see cref="FrameWrench.FrameWrenchClient"/> that is not
/// valid in the current <see cref="WebSocketState"/> (for example, sending while closed).
/// </summary>
public class WebSocketStateException : FrameWrenchException
{
    /// <summary>Gets the client state when the exception was thrown.</summary>
    public WebSocketState CurrentState { get; }

    /// <summary>Initialises a new instance with state context.</summary>
    /// <param name="state">The current connection state.</param>
    /// <param name="message">A description of the invalid operation.</param>
    public WebSocketStateException(WebSocketState state, string message)
        : base(message)
    {
        CurrentState = state;
    }
}
