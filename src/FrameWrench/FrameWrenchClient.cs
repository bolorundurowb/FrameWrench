using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading.Channels;
using FrameWrench.Core;
using FrameWrench.Internal;
using FrameWrench.Protocol;

namespace FrameWrench;

/// <summary>
/// A lightweight, client-only RFC 6455 WebSocket implementation that exposes
/// explicit frame-level control over Ping, Pong, and all other frame types.
/// Each instance supports at most one successful
/// <see cref="ConnectAsync(System.Uri, System.Threading.CancellationToken)"/> —
/// create a new client to open another connection.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Frame-level API (primary)</strong>
/// <list type="bullet">
///   <item><see cref="SendFrameAsync(FrameOpCode, ReadOnlyMemory{byte}, bool, CancellationToken)"/> — send any frame type.</item>
///   <item><see cref="ReceiveFrameAsync(System.Threading.CancellationToken)"/> — read the next frame.</item>
///   <item><see cref="GetFrameStream(System.Threading.CancellationToken)"/> — async enumeration of frames.</item>
///   <item><see cref="PingAsync(System.ReadOnlyMemory{byte}, System.TimeSpan, System.Threading.CancellationToken)"/> — send Ping and await Pong.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Message-level API (convenience)</strong>
/// <list type="bullet">
///   <item><see cref="SendTextAsync(string, System.Threading.CancellationToken)"/> / <see cref="SendBinaryAsync(System.ReadOnlyMemory{byte}, System.Threading.CancellationToken)"/> — single-frame sends.</item>
///   <item><see cref="ReceiveMessageAsync(System.Threading.CancellationToken)"/> — reassemble fragmented data into one message.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Thread-safety:</strong> The receive pump decodes frames into an in-memory channel.
/// Multiple callers may use <see cref="ReceiveFrameAsync"/>, <see cref="GetFrameStream"/>, or
/// <see cref="ReceiveMessageAsync"/> concurrently; each frame is delivered to exactly one consumer.
/// If several tasks compete for frames, ordering of logical messages remains your application's
/// responsibility. Concurrent sends from multiple threads are serialised internally via a
/// <see cref="SemaphoreSlim"/>.
/// </para>
/// <para>
/// <strong>Automatic Ping/Pong:</strong> Disabled by default.  Enable via
/// <see cref="FrameWrenchOptions.AutoPing"/>.
/// </para>
/// <para>
/// <strong>Validation policy:</strong>
/// <list type="bullet">
///   <item><em>Outbound (stricter by default):</em> When
///         <see cref="FrameWrenchOptions.ValidateOutgoingMessages"/> is <c>true</c> (default),
///         Text UTF-8 and §5.4 fragmentation ordering are checked before frames are written,
///         including reassembled fragmented Text from repeated
///         <see cref="SendFrameAsync(FrameOpCode, ReadOnlyMemory{byte}, bool, CancellationToken)"/>
///         calls. Outbound Close reason phrases are always checked. Disable outgoing checks only
///         when sending raw frames intentionally.</item>
///   <item><em>Inbound (RFC minimum by default):</em> §5.4 fragmentation ordering is always
///         enforced. Invalid UTF-8 in Text and Close reasons aborts the connection when
///         <see cref="FrameWrenchOptions.FailOnInvalidIncomingUtf8"/> is <c>true</c> (default),
///         per §8.1 and §7.4.1. Set that option to <c>false</c> only for interop with non-compliant
///         peers; you must then handle bad UTF-8 yourself.</item>
///   <item><em>Handshake:</em> <c>Sec-WebSocket-Protocol</c> is validated per §4.1 (not configurable).</item>
/// </list>
/// If outbound Text validation fails partway through a multi-frame send, prior fragments have
/// already been transmitted; treat the connection as compromised and close it.
/// </para>
/// <para>
/// <strong>Limitations:</strong>
/// <list type="bullet">
///   <item>WebSocket extensions (RFC 7692 per-message deflate, etc.) are not supported. The decoder
///         treats any non-zero RSV bit as a protocol error, so handshakes that would require extension
///         negotiation will fail. Use a different library if compression is required.</item>
///   <item>Empty unsolicited Pongs are delivered to the frame channel and <see cref="FrameReceived"/>
///         but cannot match a pending <see cref="PingAsync"/> waiter. <see cref="PingAsync"/> therefore
///         substitutes a 4-byte random payload when the caller passes an empty payload so correlation
///         still works.</item>
///   <item>Server <c>Sec-WebSocket-Protocol</c> responses are validated against
///         <see cref="FrameWrenchOptions.SubProtocols"/>; the selected value is exposed via
///         <see cref="SelectedSubProtocol"/>.</item>
///   <item>The receive channel is unbounded by default. If consumers may fall behind, set
///         <see cref="FrameWrenchOptions.ReceiveChannelCapacity"/> to enable backpressure.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class FrameWrenchClient : IDisposable, IAsyncDisposable
{
    private readonly FrameWrenchOptions _options;

    private TcpClient? _tcp;
    private Stream? _stream;
    // volatile so state transitions written by the pump task are immediately visible
    // to callers on other threads without a full memory barrier.
    private volatile WebSocketState _state = WebSocketState.None;

    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    // SingleWriter=true because only the receive pump pushes frames; this lets the channel
    // skip internal writer-concurrency bookkeeping.
    private readonly Channel<WebSocketFrame> _frameChannel =
        Channel.CreateUnbounded<WebSocketFrame>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });

    private Task? _pumpTask;
    private CancellationTokenSource? _pumpCts;

    // Interlocked guard so that concurrent Dispose / DisposeAsync / pump-exit paths
    // each run CleanUp at most once.
    private int _cleanupEntered;
    private int _activeFrameConsumers;

    /// <summary>
    /// Maps Pong payload (Base64 of echoed application data) to FIFO waiters so concurrent
    /// <see cref="PingAsync"/> calls with identical payloads still correlate correctly.
    /// </summary>
    private readonly Dictionary<string, List<PingWaiter>> _pendingPings = new(StringComparer.Ordinal);
    private readonly object _pendingPingLock = new();

    private Task? _autoPingTask;
    private CancellationTokenSource? _autoPingCts;

    private readonly IncomingUtf8MessageValidator _incomingUtf8Validator = new();
    private readonly OutgoingUtf8MessageValidator _outgoingUtf8Validator = new();

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public WebSocketState State => _state;

    /// <summary>
    /// Gets the subprotocol token selected by the server during the handshake, or <c>null</c>
    /// when the server did not include <c>Sec-WebSocket-Protocol</c>.
    /// </summary>
    /// <remarks>
    /// Populated only after <see cref="ConnectAsync(System.Uri, System.Threading.CancellationToken)"/>
    /// completes successfully. The value is validated against <see cref="FrameWrenchOptions.SubProtocols"/>
    /// per RFC 6455 §4.1.
    /// </remarks>
    public string? SelectedSubProtocol { get; private set; }

    /// <summary>
    /// Occurs when a frame is received from the server and enqueued for consumption.
    /// </summary>
    /// <remarks>
    /// Handlers run on the receive-pump thread synchronously before the frame is available
    /// from <see cref="ReceiveFrameAsync(System.Threading.CancellationToken)"/>.
    /// Keep handlers non-blocking. Not raised for frames read before subscription.
    /// </remarks>
    public event EventHandler<WebSocketFrame>? FrameReceived;

    /// <summary>Initialises a new <see cref="FrameWrenchClient"/>.</summary>
    /// <param name="options">
    /// Client configuration, or <c>null</c> to use <see cref="FrameWrenchOptions"/> defaults.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="FrameWrenchOptions.ReceiveChannelCapacity"/> is zero or negative.
    /// </exception>
    public FrameWrenchClient(FrameWrenchOptions? options = null)
    {
        _options = options ?? FrameWrenchOptions.Default;

        if (_options.ReceiveChannelCapacity is { } capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"{nameof(FrameWrenchOptions.ReceiveChannelCapacity)} must be positive when set " +
                    "(use null for unbounded).");

            _frameChannel = Channel.CreateBounded<WebSocketFrame>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                });
        }
        else
        {
            _frameChannel = Channel.CreateUnbounded<WebSocketFrame>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = true,
                });
        }
    }

    /// <summary>
    /// Opens a TCP connection, performs TLS when required, and completes the RFC 6455
    /// HTTP Upgrade handshake.
    /// </summary>
    /// <param name="uri">Target URI; scheme must be <c>ws</c> or <c>wss</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uri"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when the URI scheme is not <c>ws</c> or <c>wss</c>.</exception>
    /// <exception cref="WebSocketStateException">
    /// Thrown when <see cref="ConnectAsync(System.Uri, System.Threading.CancellationToken)"/> was
    /// already called on this instance.
    /// </exception>
    /// <exception cref="FrameWrenchException">Thrown when the TCP or TLS connection fails.</exception>
    /// <exception cref="WebSocketHandshakeException">Thrown when the HTTP Upgrade handshake fails.</exception>
    public async Task<ConnectResult> ConnectAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri is null) throw new ArgumentNullException(nameof(uri));

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != "ws" && scheme != "wss")
            throw FrameWrenchErrors.InvalidUriScheme(uri.Scheme);

        if (_state != WebSocketState.None)
            throw FrameWrenchErrors.ConnectCalledTwice();

        _state = WebSocketState.Connecting;

        bool useTls = scheme == "wss";
        int port = uri.IsDefaultPort ? (useTls ? 443 : 80) : uri.Port;

        _tcp = new TcpClient { NoDelay = true };

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_options.ConnectTimeout);

        try
        {
            var connectTask = _tcp.ConnectAsync(uri.Host, port);
            await TaskUtils.WaitAsync(connectTask, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _state = WebSocketState.Aborted;
            throw FrameWrenchErrors.TcpConnectFailed(uri.Host, port, ex);
        }

        Stream netStream = _tcp.GetStream();

        if (useTls)
        {
            var sslStream = new SslStream(
                netStream,
                leaveInnerStreamOpen: false,
                _options.RemoteCertificateValidationCallback);

            // These targets (net462, net48, netstandard2.0) require an explicit protocol
            // set — SslProtocols.None is not accepted by the old overload.  Fall back to
            // TLS 1.2, which all three runtimes support.
            await sslStream.AuthenticateAsClientAsync(
                uri.Host,
                clientCertificates: null,
                enabledSslProtocols: _options.SslProtocols == SslProtocols.None
                    ? SslProtocols.Tls12
                    : _options.SslProtocols,
                checkCertificateRevocation: false).ConfigureAwait(false);
            _stream = sslStream;
        }
        else
        {
            _stream = netStream;
        }

        var (_, keyBase64) = HandshakeHelper.GenerateKey();
        var expectedAccept = HandshakeHelper.ComputeAcceptValue(keyBase64);

        var extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _options.ExtraHeaders)
            extraHeaders[kv.Key] = kv.Value;

        if (_options.SubProtocols.Count > 0)
            extraHeaders["Sec-WebSocket-Protocol"] = string.Join(", ", _options.SubProtocols);

        var requestBytes = HandshakeHelper.BuildRequest(uri, keyBase64, extraHeaders);
        await _stream.WriteAsync(requestBytes, 0, requestBytes.Length, ct).ConfigureAwait(false);

        SelectedSubProtocol = await HandshakeHelper
            .ValidateResponseAsync(_stream, expectedAccept, ct, _options.SubProtocols)
            .ConfigureAwait(false);

        _incomingUtf8Validator.Reset();
        _outgoingUtf8Validator.Reset();
        _state = WebSocketState.Open;

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => ReceivePumpAsync(_pumpCts.Token), CancellationToken.None);

        if (_options.AutoPing)
        {
            _autoPingCts = new CancellationTokenSource();
            _autoPingTask = Task.Run(
                () => AutoPingLoopAsync(_autoPingCts.Token), CancellationToken.None);
        }

        return new ConnectResult(SelectedSubProtocol);
    }

    /// <summary>Sends a WebSocket frame constructed from the given opcode and payload.</summary>
    /// <param name="opCode">The frame opcode.</param>
    /// <param name="payload">Unmasked payload bytes (the client applies the mask on the wire).</param>
    /// <param name="isFinal">
    /// <c>true</c> to set the FIN bit; <c>false</c> to start or continue a fragmented message.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="WebSocketStateException">Thrown when <see cref="State"/> is not <see cref="WebSocketState.Open"/>.</exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown when <see cref="FrameWrenchOptions.ValidateOutgoingMessages"/> is enabled and the
    /// frame violates §5.4 ordering or §8.1 Text UTF-8 rules.
    /// </exception>
    public async Task SendFrameAsync(
        FrameOpCode opCode,
        ReadOnlyMemory<byte> payload,
        bool isFinal = true,
        CancellationToken ct = default)
    {
        EnsureOpen();
        await SendRawFrameAsync(new WebSocketFrame(opCode, isFinal, payload), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Sends a pre-built <see cref="WebSocketFrame"/> to the server.</summary>
    /// <param name="frame">The frame to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="frame"/> is <c>null</c>.</exception>
    /// <exception cref="WebSocketStateException">Thrown when <see cref="State"/> is not <see cref="WebSocketState.Open"/>.</exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown when outgoing validation is enabled and the frame violates protocol rules.
    /// </exception>
    public async Task SendFrameAsync(WebSocketFrame frame, CancellationToken ct = default)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        EnsureOpen();
        await SendRawFrameAsync(frame, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a Ping frame and waits for the server's correlated Pong response.
    /// </summary>
    /// <param name="payload">
    /// Optional payload (≤ 125 bytes) embedded in the Ping and echoed in the Pong.
    /// When <c>default</c>, a 4-byte random payload is generated so correlation always has non-empty bytes
    /// (empty Pong payloads cannot complete <see cref="PingAsync"/> correlation; RFC 6455 allows empty Pongs).
    /// </param>
    /// <param name="timeout">
    /// How long to wait for the Pong.
    /// Defaults to <see cref="FrameWrenchOptions.PingTimeout"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PingResult"/> with <see cref="PingResult.PongReceived"/> and
    /// <see cref="PingResult.Elapsed"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="payload"/> exceeds 125 bytes (RFC 6455 §5.5).
    /// </exception>
    /// <exception cref="WebSocketStateException">Thrown when <see cref="State"/> is not <see cref="WebSocketState.Open"/>.</exception>
    /// <remarks>
    /// Multiple concurrent calls with the same payload are queued in FIFO order and matched
    /// to Pongs in arrival order (the echoed application data is the correlation key).
    /// Inbound Pongs with an empty payload cannot complete a waiter; see class remarks.
    /// </remarks>
    public async Task<PingResult> PingAsync(
        ReadOnlyMemory<byte> payload = default,
        TimeSpan timeout = default,
        CancellationToken ct = default)
    {
        EnsureOpen();

        if (timeout == default) timeout = _options.PingTimeout;

        byte[] pingPayload;
        if (payload.IsEmpty)
        {
            pingPayload = new byte[4];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(pingPayload);
        }
        else
        {
            pingPayload = payload.ToArray();
        }

        if (pingPayload.Length > 125)
            throw FrameWrenchErrors.PingPayloadTooLarge();

        var key = Convert.ToBase64String(pingPayload);
        var waiter = new PingWaiter();
        lock (_pendingPingLock)
        {
            if (!_pendingPings.TryGetValue(key, out var list))
            {
                list = [];
                _pendingPings[key] = list;
            }

            list.Add(waiter);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        await SendRawFrameAsync(WebSocketFrame.Ping(pingPayload), ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await TaskUtils.WaitAsync(waiter.Task, timeoutCts.Token).ConfigureAwait(false);
            sw.Stop();
            return new PingResult(true, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new PingResult(false, sw.Elapsed);
        }
        finally
        {
            lock (_pendingPingLock)
            {
                if (_pendingPings.TryGetValue(key, out var list))
                {
                    list.Remove(waiter);
                    if (list.Count == 0)
                        _pendingPings.Remove(key);
                }
            }
        }
    }

    /// <summary>Encodes <paramref name="text"/> as UTF-8 and sends a final Text frame.</summary>
    /// <param name="text">The text to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the frame is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <c>null</c>.</exception>
    /// <exception cref="WebSocketStateException">Thrown when <see cref="State"/> is not <see cref="WebSocketState.Open"/>.</exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown when <see cref="FrameWrenchOptions.ValidateOutgoingMessages"/> is enabled and validation fails.
    /// </exception>
    public Task SendTextAsync(string text, CancellationToken ct = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        EnsureOpen();
        return SendRawFrameAsync(WebSocketFrame.Text(text), ct);
    }

    /// <summary>Sends <paramref name="data"/> as a final Binary frame.</summary>
    /// <param name="data">The binary payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the frame is written.</returns>
    /// <exception cref="WebSocketStateException">Thrown when <see cref="State"/> is not <see cref="WebSocketState.Open"/>.</exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown when <see cref="FrameWrenchOptions.ValidateOutgoingMessages"/> is enabled and
    /// a fragmented message is already in progress.
    /// </exception>
    public Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        return SendRawFrameAsync(WebSocketFrame.Binary(data), ct);
    }

    /// <summary>Reads the next frame received from the server.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next <see cref="WebSocketFrame"/>.</returns>
    /// <exception cref="FrameWrenchException">
    /// Thrown when the connection is closed and no further frames are available.
    /// </exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown when the receive pump ended due to a protocol violation (also stored as the
    /// channel completion exception).
    /// </exception>
    /// <remarks>
    /// Ping frames receive an automatic Pong response before this method returns the Ping.
    /// Close frames are delivered here before the receive pump stops.
    /// </remarks>
    public Task<WebSocketFrame> ReceiveFrameAsync(CancellationToken ct = default) =>
        ReadFrameFromChannelAsync(ct);

    /// <summary>
    /// Primary API: enumerates frames until the connection ends or <paramref name="ct"/> is cancelled.
    /// </summary>
    public IAsyncEnumerable<WebSocketFrame> ReceiveFramesAsync(CancellationToken ct = default) =>
        ReceiveFramesCoreAsync(ct);

    /// <summary>
    /// Obsolete: use <see cref="ReceiveFramesAsync"/>.
    /// </summary>
    [Obsolete("Use ReceiveFramesAsync instead.")]
    public IAsyncEnumerable<WebSocketFrame> GetFrameStream(CancellationToken ct = default) =>
        ReceiveFramesAsync(ct);

    private async IAsyncEnumerable<WebSocketFrame> ReceiveFramesCoreAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        EnterFrameConsumer();
        try
        {
            var reader = _frameChannel.Reader;
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var frame))
                    yield return frame;
            }
        }
        finally
        {
            ExitFrameConsumer();
        }
    }

    private async Task<WebSocketFrame> ReadFrameFromChannelAsync(CancellationToken ct)
    {
        EnterFrameConsumer();
        try
        {
            return await _frameChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            if (TryUnwrapWebSocketProtocolException(ex) is { } wsp)
                throw wsp;

            throw FrameWrenchErrors.ConnectionClosedNoFrames(_state);
        }
        finally
        {
            ExitFrameConsumer();
        }
    }

    private void EnterFrameConsumer()
    {
        if (!_options.SingleFrameConsumer)
            return;

        if (Interlocked.Increment(ref _activeFrameConsumers) > 1)
        {
            Interlocked.Decrement(ref _activeFrameConsumers);
            throw FrameWrenchErrors.SingleFrameConsumerViolation();
        }
    }

    private void ExitFrameConsumer()
    {
        if (_options.SingleFrameConsumer)
            Interlocked.Decrement(ref _activeFrameConsumers);
    }

    /// <summary>
    /// Reads and reassembles frames until a complete Text or Binary message is available.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="WebSocketMessage"/> with the combined payload.</returns>
    /// <exception cref="WebSocketClosedByPeerException">
    /// Thrown when a Close frame is received (including between fragments).
    /// </exception>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown on §5.4 ordering errors or when the reassembled size exceeds
    /// <see cref="FrameWrenchOptions.MaxMessagePayloadBytes"/>.
    /// </exception>
    /// <exception cref="FrameWrenchException">Thrown when the connection ends before a message completes.</exception>
    /// <remarks>
    /// Ping and Pong frames are skipped. For per-frame access including control frames, use
    /// <see cref="ReceiveFrameAsync(System.Threading.CancellationToken)"/>.
    /// </remarks>
    public async Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        var fragments = new List<WebSocketFrame>();
        FrameOpCode? msgType = null;
        int totalLen = 0;

        while (true)
        {
            var frame = await ReceiveFrameAsync(ct).ConfigureAwait(false);

            if (frame.OpCode == FrameOpCode.Close)
                throw FrameWrenchErrors.PeerClosed(
                    frame.GetCloseInfo(), nameof(ReceiveMessageAsync));

            if (frame.IsControl) continue;

            if (msgType is null)
            {
                if (frame.OpCode == FrameOpCode.Continuation)
                    throw FrameWrenchErrors.Fragmentation(
                        "Received a Continuation frame without a preceding data frame.",
                        outbound: false,
                        actual: FrameOpCode.Continuation);

                msgType = frame.OpCode;
            }
            else if (frame.OpCode != FrameOpCode.Continuation)
            {
                throw FrameWrenchErrors.Fragmentation(
                    "Interleaved message streams are not permitted (RFC 6455 §5.4).",
                    outbound: false,
                    expected: FrameOpCode.Continuation,
                    actual: frame.OpCode);
            }

            fragments.Add(frame);
            totalLen += frame.Payload.Length;

            if (totalLen > _options.MaxMessagePayloadBytes)
                throw FrameWrenchErrors.PayloadTooLarge(
                    totalLen,
                    _options.MaxMessagePayloadBytes,
                    nameof(FrameWrenchOptions.MaxMessagePayloadBytes));

            if (frame.IsFinal) break;
        }

        if (fragments.Count == 1)
            return new WebSocketMessage(msgType!.Value, fragments[0].Payload, fragments);

        var combined = new byte[totalLen];
        int offset = 0;
        foreach (var f in fragments)
        {
            f.Payload.Span.CopyTo(combined.AsSpan(offset));
            offset += f.Payload.Length;
        }

        return new WebSocketMessage(msgType!.Value, combined, fragments);
    }

    /// <summary>
    /// Sends a Close frame and waits for the peer's Close echo (RFC 6455 §7.1.2).
    /// </summary>
    /// <param name="status">Close status code.</param>
    /// <param name="reason">
    /// Optional UTF-8 reason phrase (at most 123 encoded bytes after the 2-byte status code).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the handshake finishes or times out.</returns>
    /// <remarks>
    /// No-op when <see cref="State"/> is neither <see cref="WebSocketState.Open"/> nor
    /// <see cref="WebSocketState.CloseReceived"/>. If the peer does not echo within
    /// <see cref="FrameWrenchOptions.CloseHandshakeTimeout"/>, this method returns without
    /// throwing and the transport is closed. Inspect <see cref="State"/> afterwards:
    /// <see cref="WebSocketState.Closed"/> indicates a completed handshake;
    /// <see cref="WebSocketState.CloseSent"/> or <see cref="WebSocketState.Aborted"/> indicates
    /// it did not finish cleanly.
    /// </remarks>
    public async Task<CloseResult> CloseAsync(
        WireCloseStatus status = WireCloseStatus.NormalClosure,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (_state is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return new CloseResult(false, _state);

        _state = WebSocketState.CloseSent;

        try
        {
            await SendRawFrameAsync(WebSocketFrame.Close(status, reason), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            _state = WebSocketState.Aborted;
            CleanUp();
            return new CloseResult(false, _state);
        }

        using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        closeCts.CancelAfter(_options.CloseHandshakeTimeout);

        var handshakeCompleted = false;
        try
        {
            await TaskUtils.WaitAsync(
                _pumpTask ?? Task.CompletedTask, closeCts.Token).ConfigureAwait(false);
            handshakeCompleted = _state == WebSocketState.Closed;
        }
        catch (OperationCanceledException) { }

        CleanUp();
        return new CloseResult(handshakeCompleted, _state);
    }

    /// <summary>
    /// Sends a best-effort Close frame when <see cref="State"/> is <see cref="WebSocketState.Open"/>,
    /// then releases resources.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> when a <see cref="System.Threading.SynchronizationContext"/>
    /// may be present (for example UI or legacy ASP.NET), because this method can block while
    /// sending the Close frame.
    /// </remarks>
    public void Dispose()
    {
        if (_state == WebSocketState.Open)
        {
            try
            {
                SendRawFrameAsync(WebSocketFrame.Close(), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
        CleanUp();
    }

    /// <summary>
    /// Performs an asynchronous close when open, then releases resources.
    /// </summary>
    /// <returns>A value task that completes when cleanup finishes.</returns>
    /// <remarks>
    /// Prefer this over <see cref="Dispose"/> when blocking the calling context must be avoided.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_state == WebSocketState.Open)
        {
            try { await CloseAsync().ConfigureAwait(false); }
            catch { }
        }
        CleanUp();
    }

    private async Task ReceivePumpAsync(CancellationToken ct)
    {
        Exception? completionError = null;
        try
        {
            while (!ct.IsCancellationRequested &&
                   (_state == WebSocketState.Open || _state == WebSocketState.CloseSent))
            {
                WebSocketFrame frame;
                try
                {
                    frame = await FrameDecoder
                        .ReadFrameAsync(_stream!, _options.MaxFramePayloadBytes, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (EndOfStreamException)
                {
                    if (_state == WebSocketState.Open) _state = WebSocketState.Aborted;
                    break;
                }
                catch (WebSocketProtocolException ex)
                {
                    if (_state == WebSocketState.Open) _state = WebSocketState.Aborted;
                    completionError = ex;
                    break;
                }
                catch (Exception)
                {
                    if (_state == WebSocketState.Open) _state = WebSocketState.Aborted;
                    break;
                }

                try
                {
                    switch (frame.OpCode)
                    {
                        case FrameOpCode.Ping:
                            await TrySendPongAsync(frame.Payload, ct).ConfigureAwait(false);
                            await PublishFrameAsync(frame, ct).ConfigureAwait(false);
                            break;

                        case FrameOpCode.Pong:
                            CompletePingWaiter(frame);
                            await PublishFrameAsync(frame, ct).ConfigureAwait(false);
                            break;

                        case FrameOpCode.Close:
                            CloseFrameValidator.ThrowIfInvalidOnWire(
                                frame.Payload, _options.FailOnInvalidIncomingUtf8);
                            await PublishFrameAsync(frame, ct).ConfigureAwait(false);
                            await HandleIncomingCloseAsync(frame, ct).ConfigureAwait(false);
                            return;

                        case FrameOpCode.Text:
                        case FrameOpCode.Binary:
                        case FrameOpCode.Continuation:
                            _incomingUtf8Validator.OnDataFrame(
                                frame, _options.FailOnInvalidIncomingUtf8);
                            await PublishFrameAsync(frame, ct).ConfigureAwait(false);
                            break;

                        default:
                            await PublishFrameAsync(frame, ct).ConfigureAwait(false);
                            break;
                    }
                }
                catch (WebSocketProtocolException ex)
                {
                    if (_state == WebSocketState.Open)
                        _state = WebSocketState.Aborted;
                    completionError = ex;
                    break;
                }
            }
        }
        finally
        {
            _frameChannel.Writer.TryComplete(completionError);
            if (completionError is null && _state == WebSocketState.Open)
                _state = WebSocketState.Aborted;
        }
    }

    private ValueTask PublishFrameAsync(WebSocketFrame frame, CancellationToken ct)
    {
        // Fast path: unbounded channels and bounded channels with room accept the write
        // synchronously. Only fall back to WriteAsync when the bounded channel is full, so
        // the receive pump applies backpressure to the TCP stream instead of dropping frames.
        if (_frameChannel.Writer.TryWrite(frame))
        {
            FrameReceived?.Invoke(this, frame);
            return default;
        }

        return PublishFrameSlowAsync(frame, ct);
    }

    private async ValueTask PublishFrameSlowAsync(WebSocketFrame frame, CancellationToken ct)
    {
        await _frameChannel.Writer.WriteAsync(frame, ct).ConfigureAwait(false);
        FrameReceived?.Invoke(this, frame);
    }

    private void CompletePingWaiter(WebSocketFrame pong)
    {
        // RFC 6455 §5.5.4 explicitly allows unsolicited Pongs (heartbeats) with empty payloads.
        // We cannot correlate an empty Pong with any PingAsync waiter (correlation is by echoed
        // application data), so we simply skip the waiter lookup. The Pong is still surfaced to
        // the application via PublishFrame above, so subscribers see every Pong on the wire.
        if (pong.Payload.IsEmpty) return;
        var key = PingPayloadToCorrelationKey(pong.Payload);
        lock (_pendingPingLock)
        {
            if (!_pendingPings.TryGetValue(key, out var list) || list.Count == 0)
                return;

            var waiter = list[0];
            list.RemoveAt(0);
            if (list.Count == 0)
                _pendingPings.Remove(key);

            waiter.SetResult();
        }
    }

    private static string PingPayloadToCorrelationKey(ReadOnlyMemory<byte> payload)
    {
        if (MemoryMarshal.TryGetArray(payload, out var seg) && seg.Array is not null)
            return Convert.ToBase64String(seg.Array, seg.Offset, seg.Count);

        return Convert.ToBase64String(payload.ToArray());
    }

    private async Task TrySendPongAsync(ReadOnlyMemory<byte> pingPayload, CancellationToken ct)
    {
        try
        {
            await SendRawFrameAsync(WebSocketFrame.Pong(pingPayload), ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort Pong (RFC 6455 §5.5.3 only says a peer SHOULD respond promptly).
            // Failures are swallowed because the connection may already be tearing down; if the
            // failure is permanent the next inbound read on the pump will surface it and move the
            // state to Aborted. Applications that need stronger signalling can subscribe to
            // FrameReceived for the inbound Ping and send their own Pong via SendFrameAsync.
        }
    }

    private async Task HandleIncomingCloseAsync(WebSocketFrame frame, CancellationToken ct)
    {
        var closeInfo = frame.GetCloseInfo();
        var echoFrame = CloseFrameValidator.CreateEchoFrame(closeInfo);

        if (_state == WebSocketState.CloseSent)
        {
            _state = WebSocketState.Closed;
        }
        else if (_state == WebSocketState.Open)
        {
            _state = WebSocketState.CloseReceived;
            try
            {
                await SendRawFrameAsync(echoFrame, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort Close echo.
            }
            _state = WebSocketState.Closed;
        }
    }

    private async Task AutoPingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _state == WebSocketState.Open)
            {
                await Task.Delay(_options.KeepAliveInterval, ct).ConfigureAwait(false);
                if (_state != WebSocketState.Open) break;

                var pingResult = await PingAsync(
                    timeout: _options.PingTimeout, ct: ct).ConfigureAwait(false);

                if (!pingResult.PongReceived)
                {
                    _ = CloseAsync(WireCloseStatus.GoingAway, "Ping timed out",
                        CancellationToken.None);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Auto-ping loop ended with an unexpected error; connection state reflects outcome.
        }
    }

    private async Task SendRawFrameAsync(WebSocketFrame frame, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (frame.OpCode == FrameOpCode.Close)
            {
                CloseFrameValidator.ThrowIfInvalidOnWire(frame.Payload, validateUtf8: true);
            }
            else if (_options.ValidateOutgoingMessages
                && (frame.OpCode == FrameOpCode.Text
                    || frame.OpCode == FrameOpCode.Binary
                    || frame.OpCode == FrameOpCode.Continuation))
            {
                // Stricter than RFC requires of a sender: §5.4 ordering + §8.1 Text UTF-8.
                _outgoingUtf8Validator.OnDataFrame(frame);
            }

            await FrameEncoder.WriteAsync(_stream!, frame, masked: true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void EnsureOpen([CallerMemberName] string? caller = null)
    {
        if (_state != WebSocketState.Open)
            throw FrameWrenchErrors.InvalidState(
                _state,
                caller ?? "operation",
                WebSocketState.Open);
    }

    private static WebSocketProtocolException? TryUnwrapWebSocketProtocolException(
        Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is WebSocketProtocolException wsp)
                return wsp;
        }

        return null;
    }

    private void CleanUp()
    {
        if (Interlocked.Exchange(ref _cleanupEntered, 1) != 0)
            return;

        _incomingUtf8Validator.Reset();
        _outgoingUtf8Validator.Reset();

        try { _autoPingCts?.Cancel(); } catch { }
        try { _pumpCts?.Cancel(); } catch { }

        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }

        _frameChannel.Writer.TryComplete();

        if (_state is not (WebSocketState.Closed or WebSocketState.Aborted))
            _state = WebSocketState.Closed;
    }

    /// <summary>
    /// Wraps a <see cref="TaskCompletionSource{TResult}"/> so an async <see cref="PingAsync"/>
    /// call can efficiently await Pong arrival without polling or blocking a thread.
    /// </summary>
    private sealed class PingWaiter
    {
        private readonly TaskCompletionSource<bool> _tcs =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The task that completes when the matching Pong is received.</summary>
        public Task Task => _tcs.Task;

        /// <summary>Signals that the matching Pong has been received.</summary>
        public void SetResult() => _tcs.TrySetResult(true);
    }
}
