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
/// Each instance supports at most one successful <see cref="ConnectAsync(Uri, CancellationToken)"/> —
/// create a new client to open another connection.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Frame-level API (primary)</strong>
/// <list type="bullet">
///   <item><see cref="SendFrameAsync(FrameOpCode, ReadOnlyMemory{byte}, bool, CancellationToken)"/> - sends any frame type.</item>
///   <item><see cref="ReceiveFrameAsync"/> - reads the next frame from the server.</item>
///   <item><see cref="GetFrameStream"/> - returns an async stream of frames.</item>
///   <item><see cref="PingAsync"/> - sends a Ping and awaits the correlated Pong.</item>
/// </list>
/// </para>
/// <para>
/// <strong>Message-level API (convenience)</strong>
/// <list type="bullet">
///   <item><see cref="SendTextAsync"/> / <see cref="SendBinaryAsync"/> - single-call sends.</item>
///   <item><see cref="ReceiveMessageAsync"/> - reassembles fragmented frames into one message.</item>
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
/// <strong>UTF-8 (RFC 6455 §8.1):</strong> Complete Text messages from the server are validated
/// as well-formed UTF-8 before frames are delivered; invalid UTF-8 fails the connection with
/// <see cref="WebSocketProtocolException"/>. Close frame reason phrases are validated similarly.
/// Final Text frames sent with <see cref="SendFrameAsync(WebSocketFrame, CancellationToken)"/> are
/// checked when <see cref="WebSocketFrame.IsFinal"/> is <c>true</c>; fragmented Text you send must
/// be valid UTF-8 across the whole message (the library does not reassemble outgoing Text for validation).
/// </para>
/// </remarks>
public sealed class FrameWrenchClient : IDisposable, IAsyncDisposable
{
    private readonly FrameWrenchOptions _options;

    private TcpClient? _tcp;
    private Stream? _stream;
    private volatile WebSocketState _state = WebSocketState.None;

    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private readonly Channel<WebSocketFrame> _frameChannel =
        Channel.CreateUnbounded<WebSocketFrame>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
        });

    private Task? _pumpTask;
    private CancellationTokenSource? _pumpCts;

    private int _cleanupEntered;

    /// <summary>
    /// Maps Pong payload (Base64 of echoed application data) to FIFO waiters so concurrent
    /// <see cref="PingAsync"/> calls with identical payloads still correlate correctly.
    /// </summary>
    private readonly Dictionary<string, List<PingWaiter>> _pendingPings = new(StringComparer.Ordinal);
    private readonly object _pendingPingLock = new();

    private Task? _autoPingTask;
    private CancellationTokenSource? _autoPingCts;

    private readonly IncomingUtf8MessageValidator _incomingUtf8Validator = new();

    /// <summary>The current connection state.</summary>
    public WebSocketState State => _state;

    /// <summary>
    /// Raised each time a frame is received from the server.
    /// This event fires on the receive-pump thread; keep handlers brief.
    /// </summary>
    public event EventHandler<WebSocketFrame>? FrameReceived;

    /// <summary>
    /// Initialises a new <see cref="FrameWrenchClient"/> with optional configuration.
    /// </summary>
    /// <param name="options">Client configuration; pass <c>null</c> for defaults.</param>
    public FrameWrenchClient(FrameWrenchOptions? options = null)
    {
        _options = options ?? new FrameWrenchOptions();
    }

    /// <summary>
    /// Connects to the WebSocket server at <paramref name="uri"/> and completes
    /// the RFC 6455 HTTP Upgrade handshake.
    /// </summary>
    /// <param name="uri">WebSocket URI; scheme must be <c>ws</c> or <c>wss</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when the URI scheme is invalid.</exception>
    /// <exception cref="WebSocketHandshakeException">Thrown if the handshake fails.</exception>
    public async Task ConnectAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri is null) throw new ArgumentNullException(nameof(uri));

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != "ws" && scheme != "wss")
            throw new ArgumentException(
                $"URI scheme must be 'ws' or 'wss' (got '{uri.Scheme}').", nameof(uri));

        if (_state != WebSocketState.None)
            throw new WebSocketStateException(
                _state, "ConnectAsync may only be called once on a new client instance.");

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
            throw new FrameWrenchException($"TCP connection to {uri.Host}:{port} failed.", ex);
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

        var extraHeaders = new Dictionary<string, string>(
            _options.ExtraHeaders, StringComparer.OrdinalIgnoreCase);

        if (_options.SubProtocols.Count > 0)
            extraHeaders["Sec-WebSocket-Protocol"] = string.Join(", ", _options.SubProtocols);

        var requestBytes = HandshakeHelper.BuildRequest(uri, keyBase64, extraHeaders);
        await _stream.WriteAsync(requestBytes, 0, requestBytes.Length, ct).ConfigureAwait(false);

        _stream = await HandshakeHelper.ValidateResponseAsync(_stream, expectedAccept, ct)
            .ConfigureAwait(false);

        _incomingUtf8Validator.Reset();
        _state = WebSocketState.Open;

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => ReceivePumpAsync(_pumpCts.Token), CancellationToken.None);

        if (_options.AutoPing)
        {
            _autoPingCts = new CancellationTokenSource();
            _autoPingTask = Task.Run(
                () => AutoPingLoopAsync(_autoPingCts.Token), CancellationToken.None);
        }
    }

    /// <summary>
    /// Sends a single WebSocket frame to the server.
    /// </summary>
    /// <param name="opCode">The frame opcode.</param>
    /// <param name="payload">The unmasked payload bytes.</param>
    /// <param name="isFinal">
    /// <c>true</c> to set the FIN bit.  Pass <c>false</c> to start or continue a
    /// fragmented message.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Sends a pre-built <see cref="WebSocketFrame"/> to the server.
    /// </summary>
    /// <param name="frame">The frame to send.</param>
    /// <param name="ct">Cancellation token.</param>
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
    /// <c>(true, roundtrip)</c> if the Pong arrived in time;
    /// <c>(false, elapsed)</c> on timeout.
    /// </returns>
    /// <remarks>
    /// Multiple concurrent calls with the same payload are queued in FIFO order and matched
    /// to Pongs in the same order (the echoed application data is the correlation key).
    /// </remarks>
    public async Task<(bool pongReceived, TimeSpan roundtrip)> PingAsync(
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
            throw new ArgumentException("Ping payload must not exceed 125 bytes (RFC 6455).", nameof(payload));

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
            return (true, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return (false, sw.Elapsed);
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

    /// <summary>Encodes <paramref name="text"/> as UTF-8 and sends it as a Text frame.</summary>
    public Task SendTextAsync(string text, CancellationToken ct = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        EnsureOpen();
        return SendRawFrameAsync(WebSocketFrame.Text(text), ct);
    }

    /// <summary>Sends <paramref name="data"/> as a Binary frame.</summary>
    public Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        return SendRawFrameAsync(WebSocketFrame.Binary(data), ct);
    }

    /// <summary>
    /// Reads the next frame from the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next <see cref="WebSocketFrame"/> from the server.</returns>
    public async Task<WebSocketFrame> ReceiveFrameAsync(CancellationToken ct = default)
    {
        try
        {
            return await _frameChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            if (TryUnwrapWebSocketProtocolException(ex) is { } wsp)
                throw wsp;

            throw new FrameWrenchException(
                $"The WebSocket connection is closed (state={_state}). No further frames are available.",
                ex);
        }
    }

    /// <summary>
    /// Returns an async stream of all frames received from the server.
    /// Yields until the connection is closed or <paramref name="ct"/> fires.
    /// </summary>
    public async IAsyncEnumerable<WebSocketFrame> GetFrameStream(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = _frameChannel.Reader;
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out var frame))
                yield return frame;
        }
    }

    /// <summary>
    /// Reads frames until a complete message has been reassembled, handling
    /// fragmented messages transparently.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="WebSocketMessage"/> containing the full payload.</returns>
    /// <exception cref="WebSocketClosedByPeerException">
    /// Thrown when a Close frame is received (including between fragments of a message).
    /// </exception>
    public async Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        var fragments = new List<WebSocketFrame>();
        FrameOpCode? msgType = null;
        int totalLen = 0;

        while (true)
        {
            var frame = await ReceiveFrameAsync(ct).ConfigureAwait(false);

            if (frame.OpCode == FrameOpCode.Close)
            {
                frame.GetCloseData(out var closeStatus, out var closeReason);
                throw new WebSocketClosedByPeerException(closeStatus, closeReason);
            }

            if (frame.IsControl) continue;

            if (msgType is null)
            {
                if (frame.OpCode == FrameOpCode.Continuation)
                    throw new WebSocketProtocolException(
                        "Received a Continuation frame without a preceding data frame.");

                msgType = frame.OpCode;
            }
            else if (frame.OpCode != FrameOpCode.Continuation)
            {
                throw new WebSocketProtocolException(
                    $"Expected a Continuation frame but received {frame.OpCode}. " +
                    "Interleaved message streams are not permitted (RFC 6455 §5.4).");
            }

            fragments.Add(frame);
            totalLen += frame.Payload.Length;

            if (totalLen > _options.MaxMessagePayloadBytes)
                throw new WebSocketProtocolException(
                    $"Reassembled message ({totalLen:N0} bytes) exceeds the configured " +
                    $"maximum of {_options.MaxMessagePayloadBytes:N0} bytes.");

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
    /// Initiates the RFC 6455 closing handshake: sends a Close frame and waits
    /// for the peer's echoing Close frame.
    /// </summary>
    /// <param name="status">Close status code (default: Normal Closure).</param>
    /// <param name="reason">Optional UTF-8 reason phrase (max 123 encoded bytes).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CloseAsync(
        WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (_state is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;

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
            return;
        }

        using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        closeCts.CancelAfter(_options.CloseHandshakeTimeout);

        try
        {
            await TaskUtils.WaitAsync(
                _pumpTask ?? Task.CompletedTask, closeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        CleanUp();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Prefer <see cref="DisposeAsync"/> when a <see cref="System.Threading.SynchronizationContext"/>
    /// may be present (for example UI or legacy ASP.NET), because this method can block while sending the close frame.
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

    /// <inheritdoc/>
    /// <remarks>
    /// Use this overload instead of <see cref="Dispose"/> when blocking the calling context must be avoided.
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
                            PublishFrame(frame);
                            break;

                        case FrameOpCode.Pong:
                            CompletePingWaiter(frame);
                            PublishFrame(frame);
                            break;

                        case FrameOpCode.Close:
                            Utf8Validator.ThrowIfInvalidCloseReason(frame.Payload);
                            PublishFrame(frame);
                            await HandleIncomingCloseAsync(frame, ct).ConfigureAwait(false);
                            return;

                        case FrameOpCode.Text:
                        case FrameOpCode.Binary:
                        case FrameOpCode.Continuation:
                            _incomingUtf8Validator.OnDataFrame(frame);
                            PublishFrame(frame);
                            break;

                        default:
                            PublishFrame(frame);
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

    private void PublishFrame(WebSocketFrame frame)
    {
        _frameChannel.Writer.TryWrite(frame);
        FrameReceived?.Invoke(this, frame);
    }

    private void CompletePingWaiter(WebSocketFrame pong)
    {
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
            // Best-effort Pong; connection may already be closing.
        }
    }

    private async Task HandleIncomingCloseAsync(WebSocketFrame frame, CancellationToken ct)
    {
        frame.GetCloseData(out var status, out _);

        if (_state == WebSocketState.CloseSent)
        {
            _state = WebSocketState.Closed;
        }
        else if (_state == WebSocketState.Open)
        {
            _state = WebSocketState.CloseReceived;
            try
            {
                await SendRawFrameAsync(
                    WebSocketFrame.Close(status ?? WebSocketCloseStatus.NormalClosure), ct)
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

                var (received, _) = await PingAsync(
                    timeout: _options.PingTimeout, ct: ct).ConfigureAwait(false);

                if (!received)
                {
                    _ = CloseAsync(WebSocketCloseStatus.GoingAway, "Ping timed out",
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
        if (frame.OpCode == FrameOpCode.Text && frame.IsFinal)
            Utf8Validator.ThrowIfInvalidUtf8(frame.Payload.Span);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
            throw new WebSocketStateException(
                _state,
                $"{caller} requires an open connection (current state: {_state}).");
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
    /// Wraps a <see cref="TaskCompletionSource{TResult}"/> used to correlate
    /// a received Pong with its outstanding <see cref="PingAsync"/> call.
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
