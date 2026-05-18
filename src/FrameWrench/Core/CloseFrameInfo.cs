namespace FrameWrench.Core;

/// <summary>
/// Parsed fields from a Close frame payload (RFC 6455 §7.4).
/// </summary>
public readonly struct CloseFrameInfo
{
    /// <summary>Initialises parsed Close frame data.</summary>
    public CloseFrameInfo(ushort? statusCode, WireCloseStatus? status, string reason)
    {
        StatusCode = statusCode;
        Status = status;
        Reason = reason ?? string.Empty;
    }

    /// <summary>Raw 16-bit status code from the payload, if at least 2 bytes were present.</summary>
    public ushort? StatusCode { get; }

    /// <summary>
    /// Known <see cref="WireCloseStatus"/> when <see cref="StatusCode"/> is a registered code;
    /// <c>null</c> for application-defined codes (3000–4999) even though <see cref="StatusCode"/> is set.
    /// </summary>
    public WireCloseStatus? Status { get; }

    /// <summary>UTF-8 reason phrase after the status code, or empty.</summary>
    public string Reason { get; }

    /// <summary><c>true</c> when the payload is empty or only partial.</summary>
    public bool IsEmpty => StatusCode is null && string.IsNullOrEmpty(Reason);
}
