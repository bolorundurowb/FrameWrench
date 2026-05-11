using System.Net.Security;
using System.Security.Authentication;

namespace FrameWrench;

/// <summary>
/// Configuration options for <see cref="FrameWrenchClient"/>.
/// </summary>
public sealed class FrameWrenchOptions
{
    /// <summary>
    /// TCP connect timeout.  <see cref="Timeout.InfiniteTimeSpan"/> means no timeout.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Additional HTTP headers sent in the Upgrade request (e.g., <c>Authorization</c>,
    /// <c>Origin</c>).  Keys and values must be valid HTTP header names/values.
    /// </summary>
    public IDictionary<string, string> ExtraHeaders { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sub-protocols to advertise in the <c>Sec-WebSocket-Protocol</c> request header.
    /// Leave empty to advertise no sub-protocol.
    /// </summary>
    public IList<string> SubProtocols { get; set; } = new List<string>();

    /// <summary>
    /// TLS protocols to allow for <c>wss://</c> connections.
    /// Default: <see cref="SslProtocols.None"/> (lets the OS choose, typically TLS 1.2/1.3).
    /// </summary>
    public SslProtocols SslProtocols { get; set; } = SslProtocols.None;

    /// <summary>
    /// Custom certificate-validation callback.  When <c>null</c> (default) the system
    /// default validation is used.  Set to <c>(_, _, _, _) => true</c> for development
    /// environments where self-signed certificates are acceptable.
    /// </summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    /// <summary>
    /// Maximum accepted incoming frame payload in bytes.
    /// Frames that exceed this size cause a <see cref="Core.WebSocketProtocolException"/>.
    /// Default: 64 MiB.
    /// </summary>
    public int MaxFramePayloadBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Maximum accepted incoming message payload when calling
    /// <see cref="FrameWrenchClient.ReceiveMessageAsync"/>.
    /// Default: 64 MiB.
    /// </summary>
    public int MaxMessagePayloadBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// When <c>true</c>, the client automatically sends a Ping frame at every
    /// <see cref="KeepAliveInterval"/> and expects a Pong in return.
    /// Default: <c>false</c> (disabled).
    /// </summary>
    /// <remarks>
    /// If you prefer manual control (the recommended, frame-level approach), leave
    /// this disabled and call <see cref="FrameWrenchClient.PingAsync"/> directly.
    /// </remarks>
    public bool AutoPing { get; set; } = false;

    /// <summary>
    /// Interval between automatic Ping frames when <see cref="AutoPing"/> is enabled.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait for a Pong reply before considering the connection dead.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait for the peer's Close echo after sending a Close frame.
    /// If the timeout elapses the TCP connection is closed unconditionally.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan CloseHandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
