using System.Net.Security;
using System.Security.Authentication;

namespace FrameWrench;

/// <summary>
/// Configuration for a <see cref="FrameWrenchClient"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// Options are read when the corresponding operation runs (for example,
/// <see cref="ConnectTimeout"/> at connect time,
/// <see cref="MaxFramePayloadBytes"/> on each decoded frame). Mutating a property after
/// <see cref="FrameWrenchClient.ConnectAsync(System.Uri, System.Threading.CancellationToken)"/>
/// has started may produce mixed behaviour; treat each client as owning its options for the
/// lifetime of a connection.
/// </para>
/// <para>
/// UTF-8 and fragmentation validation are controlled by
/// <see cref="ValidateOutgoingMessages"/> and <see cref="FailOnInvalidIncomingUtf8"/>.
/// See <see cref="FrameWrenchClient"/> remarks for the full validation policy.
/// </para>
/// </remarks>
public sealed class FrameWrenchOptions
{
    /// <summary>
    /// Gets or sets the TCP connect timeout.
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables the timeout.
    /// </summary>
    /// <remarks>Default: 30 seconds.</remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets additional HTTP headers included in the Upgrade request
    /// (for example <c>Authorization</c> or <c>Origin</c>).
    /// Keys and values must be valid HTTP field names and values.
    /// </summary>
    /// <remarks>
    /// <para>Default: an empty dictionary.</para>
    /// <para>
    /// Treat the assigned collection as owned by this options instance (or a single client).
    /// Do not share the same dictionary across concurrent connections unless you snapshot it;
    /// <see cref="FrameWrenchClient.ConnectAsync(System.Uri, System.Threading.CancellationToken)"/>
    /// copies entries at connect time only.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> ExtraHeaders { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the subprotocol tokens advertised in the
    /// <c>Sec-WebSocket-Protocol</c> request header.
    /// An empty list advertises no subprotocol.
    /// </summary>
    /// <remarks>
    /// <para>Default: an empty list.</para>
    /// <para>
    /// The server's selection is validated per RFC 6455 §4.1 and exposed on
    /// <see cref="FrameWrenchClient.SelectedSubProtocol"/> after a successful handshake.
    /// Same ownership guidance as <see cref="ExtraHeaders"/> — avoid mutating a shared list
    /// while connections are in flight.
    /// </para>
    /// </remarks>
    public IList<string> SubProtocols { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the TLS protocol versions allowed for <c>wss://</c> connections.
    /// </summary>
    /// <remarks>
    /// <para>Default: <see cref="SslProtocols.None"/>.</para>
    /// <para>
    /// On <c>net462</c>, <c>net48</c>, and <c>netstandard2.0</c>, the
    /// <see cref="SslStream"/> overload used by <see cref="FrameWrenchClient"/> does not accept
    /// <see cref="SslProtocols.None"/>; the client falls back to
    /// <see cref="SslProtocols.Tls12"/> when None is configured.
    /// </para>
    /// </remarks>
    public SslProtocols SslProtocols { get; set; } = SslProtocols.None;

    /// <summary>
    /// Gets or sets the callback used to validate the server certificate for
    /// <c>wss://</c> connections, or <c>null</c> to use the system default.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>null</c> (system validation).</para>
    /// <para>
    /// For development with self-signed certificates only, you may set
    /// <c>(_, _, _, _) =&gt; true</c>. Do not disable validation in production.
    /// </para>
    /// </remarks>
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    /// <summary>
    /// Gets or sets the maximum permitted size in bytes of a single incoming frame payload.
    /// Larger frames cause <see cref="Core.WebSocketProtocolException"/> during decode.
    /// </summary>
    /// <remarks>Default: 64 MiB (67,108,864 bytes).</remarks>
    public int MaxFramePayloadBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum permitted size in bytes of a reassembled message returned by
    /// <see cref="FrameWrenchClient.ReceiveMessageAsync(System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <remarks>Default: 64 MiB (67,108,864 bytes).</remarks>
    public int MaxMessagePayloadBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Gets or sets whether the client sends periodic Ping frames and treats a missing Pong
    /// as a dead connection.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>false</c>.</para>
    /// <para>
    /// When disabled (recommended for explicit control), call
    /// <see cref="FrameWrenchClient.PingAsync(System.ReadOnlyMemory{byte}, System.TimeSpan, System.Threading.CancellationToken)"/>
    /// manually. When enabled, uses <see cref="KeepAliveInterval"/> and
    /// <see cref="PingTimeout"/>.
    /// </para>
    /// </remarks>
    public bool AutoPing { get; set; } = false;

    /// <summary>
    /// Gets or sets the interval between automatic Ping frames when
    /// <see cref="AutoPing"/> is enabled.
    /// </summary>
    /// <remarks>Default: 30 seconds.</remarks>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long the client waits for a Pong after sending a Ping before treating
    /// the connection as failed (applies to <see cref="FrameWrenchClient.PingAsync"/> and
    /// <see cref="AutoPing"/>).
    /// </summary>
    /// <remarks>Default: 10 seconds.</remarks>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long <see cref="FrameWrenchClient.CloseAsync"/> waits for the peer's
    /// Close frame echo after the local Close frame is sent.
    /// </summary>
    /// <remarks>
    /// <para>Default: 5 seconds.</para>
    /// <para>
    /// When the timeout elapses, the method returns without throwing and the TCP connection
    /// is closed. Inspect <see cref="FrameWrenchClient.State"/> to determine whether the
    /// handshake completed cleanly.
    /// </para>
    /// </remarks>
    public TimeSpan CloseHandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of received frames buffered before the receive pump
    /// applies backpressure, or <c>null</c> for an unbounded buffer.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>null</c> (unbounded).</para>
    /// <para>
    /// When set to a positive value, the pump asynchronously waits before reading the next
    /// frame if the channel is full, allowing the TCP receive window to constrain a fast peer.
    /// Must be configured before <see cref="FrameWrenchClient"/> construction; the client reads
    /// this value only in its constructor.
    /// </para>
    /// </remarks>
    public int? ReceiveChannelCapacity { get; set; }

    /// <summary>
    /// Gets or sets whether outbound data frames are validated for RFC 6455 §5.4 fragmentation
    /// ordering and §8.1 Text UTF-8 (including reassembled fragmented Text) before being written.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>true</c>.</para>
    /// <para>
    /// Outbound Close reason phrases are always validated regardless of this flag.
    /// Set to <c>false</c> only when sending raw frames via
    /// <see cref="FrameWrenchClient.SendFrameAsync(FrameWrench.Core.FrameOpCode, System.ReadOnlyMemory{byte}, bool, System.Threading.CancellationToken)"/>
    /// and accepting responsibility for wire compliance.
    /// </para>
    /// </remarks>
    public bool ValidateOutgoingMessages { get; set; } = true;

    /// <summary>
    /// Gets or sets whether invalid UTF-8 in inbound Text messages and Close reason phrases
    /// aborts the connection per RFC 6455 §8.1 and §7.4.1.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>true</c>.</para>
    /// <para>
    /// §5.4 fragmentation ordering is always enforced regardless of this flag.
    /// Setting this to <c>false</c> is not RFC-compliant; use only for interop with broken
    /// peers when your application validates or discards payloads itself.
    /// </para>
    /// </remarks>
    public bool FailOnInvalidIncomingUtf8 { get; set; } = true;
}
