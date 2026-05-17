using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// Base exception for all FrameWrench errors with structured, actionable detail.
/// </summary>
public class FrameWrenchException : Exception
{
    /// <summary>Initialises with structured error detail.</summary>
    public FrameWrenchException(FrameWrenchErrorDetail detail)
        : base(FrameWrenchErrorFormatter.Format(detail))
    {
        Detail = detail;
    }

    /// <summary>Initialises with detail and an inner exception.</summary>
    public FrameWrenchException(FrameWrenchErrorDetail detail, Exception inner)
        : base(FrameWrenchErrorFormatter.Format(detail), inner)
    {
        Detail = detail;
    }

    /// <summary>Structured error information for logging and UI.</summary>
    public FrameWrenchErrorDetail Detail { get; }

    /// <summary>Stable error code (same as <see cref="FrameWrenchErrorDetail.Code"/>).</summary>
    public string ErrorCode => Detail.Code;

    /// <inheritdoc/>
    public override string ToString() => FrameWrenchErrorFormatter.FormatWithInner(this);
}

/// <summary>HTTP Upgrade handshake failed.</summary>
public class WebSocketHandshakeException : FrameWrenchException
{
    /// <summary>HTTP status line when available.</summary>
    public string? StatusLine { get; }

    internal WebSocketHandshakeException(FrameWrenchErrorDetail detail, string? statusLine = null)
        : base(detail)
    {
        StatusLine = statusLine;
    }
}

/// <summary>
/// Peer sent Close while reading logical messages via
/// <see cref="FrameWrench.FrameWrenchClient.ReceiveMessageAsync"/>.
/// </summary>
public class WebSocketClosedByPeerException : FrameWrenchException
{
    /// <summary>Parsed Close frame fields.</summary>
    public CloseFrameInfo CloseInfo { get; }

    /// <summary>UTF-8 reason phrase (convenience; same as <see cref="CloseFrameInfo.Reason"/>).</summary>
    public string CloseReason => CloseInfo.Reason;

    internal WebSocketClosedByPeerException(CloseFrameInfo closeInfo, FrameWrenchErrorDetail detail)
        : base(detail)
    {
        CloseInfo = closeInfo;
    }
}

/// <summary>RFC 6455 protocol violation.</summary>
public class WebSocketProtocolException : FrameWrenchException
{
    /// <summary>Violation category for filtering.</summary>
    public ProtocolViolationKind Kind { get; }

    internal WebSocketProtocolException(ProtocolViolationKind kind, FrameWrenchErrorDetail detail)
        : base(detail)
    {
        Kind = kind;
    }

    internal WebSocketProtocolException(
        ProtocolViolationKind kind,
        FrameWrenchErrorDetail detail,
        Exception inner)
        : base(detail, inner)
    {
        Kind = kind;
    }
}

/// <summary>Operation invalid for the current <see cref="WebSocketState"/>.</summary>
public class WebSocketStateException : FrameWrenchException
{
    /// <summary>State when the exception was thrown.</summary>
    public WebSocketState CurrentState { get; }

    internal WebSocketStateException(WebSocketState state, FrameWrenchErrorDetail detail)
        : base(detail)
    {
        CurrentState = state;
    }
}
