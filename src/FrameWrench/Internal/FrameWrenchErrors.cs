using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>Central factory for actionable FrameWrench exceptions.</summary>
internal static class FrameWrenchErrors
{
    private const string Rfc6455 = "RFC 6455";
    private const string RfcBase = "https://datatracker.ietf.org/doc/html/rfc6455";

    public static WebSocketProtocolException MaskedServerFrame(WebSocketState state) =>
        new(
            ProtocolViolationKind.MaskedServerFrame,
            new FrameWrenchErrorDetail(
                "FW-PROTO-MASKED-SERVER-FRAME",
                "received a masked frame from the server",
                "The server sent a frame with MASK=1. In RFC 6455, only clients mask outbound frames; " +
                "server-to-client frames must be unmasked.",
                ErrorContext.Create(("connectionState", ErrorContext.FormatState(state))),
                new[]
                {
                    "Confirm the URL is a WebSocket endpoint (ws/wss), not plain HTTP or raw TCP.",
                    "If you own the server, ensure it does not mask frames sent to clients.",
                    "Capture a wire log (e.g. Wireshark) if the peer is a third-party service.",
                },
                "RFC 6455 §5.1",
                $"{RfcBase}#section-5.1"));

    public static WebSocketProtocolException NonZeroRsv(bool rsv1, bool rsv2, bool rsv3) =>
        new(
            ProtocolViolationKind.NonZeroRsv,
            new FrameWrenchErrorDetail(
                "FW-PROTO-NON-ZERO-RSV",
                "received a frame with non-zero RSV bits",
                "RSV1–RSV3 must be 0 unless a per-message extension was negotiated. " +
                "FrameWrench does not negotiate extensions (e.g. permessage-deflate).",
                ErrorContext.Create(
                    ("rsv1", rsv1.ToString()),
                    ("rsv2", rsv2.ToString()),
                    ("rsv3", rsv3.ToString())),
                new[]
                {
                    "Disable compression extensions on the server for this client.",
                    "Use a WebSocket library that supports the required extension if compression is mandatory.",
                },
                "RFC 6455 §5.2",
                $"{RfcBase}#section-5.2"));

    public static WebSocketProtocolException ReservedOpcode(byte opcode, bool fin) =>
        new(
            ProtocolViolationKind.ReservedOpcode,
            new FrameWrenchErrorDetail(
                "FW-PROTO-RESERVED-OPCODE",
                $"received reserved opcode 0x{opcode:X1}",
                "Reserved opcodes require prior extension negotiation. The connection must be failed.",
                ErrorContext.Create(
                    ("opcode", $"0x{opcode:X1}"),
                    ("fin", fin.ToString())),
                new[]
                {
                    "Verify the peer implements RFC 6455 without proprietary opcode extensions.",
                    "Capture the offending frame in a protocol trace for the server vendor.",
                },
                "RFC 6455 §5.2",
                $"{RfcBase}#section-5.2"));

    public static WebSocketProtocolException FragmentedControlFrame(FrameOpCode op) =>
        new(
            ProtocolViolationKind.FragmentedControlFrame,
            new FrameWrenchErrorDetail(
                "FW-PROTO-FRAGMENTED-CONTROL",
                $"control frame {op} has FIN cleared",
                "Close, Ping, and Pong frames must not be fragmented (FIN must be 1).",
                ErrorContext.Create(("opcode", ErrorContext.FormatOpcode(op))),
                suggestions: new[] { "Treat the peer as non-compliant and close the connection." },
                rfcSection: "RFC 6455 §5.5",
                rfcUrl: $"{RfcBase}#section-5.5"));

    public static WebSocketProtocolException ControlPayloadTooLarge(long bytes) =>
        new(
            ProtocolViolationKind.ControlPayloadTooLarge,
            new FrameWrenchErrorDetail(
                "FW-PROTO-CONTROL-PAYLOAD-LARGE",
                "control frame payload exceeds 125 bytes",
                $"Control frames may carry at most 125 bytes of payload; received {bytes} bytes.",
                ErrorContext.Create(("payloadBytes", bytes.ToString())),
                suggestions: new[] { "Inspect the peer implementation for incorrect Close/Ping/Pong encoding." },
                rfcSection: "RFC 6455 §5.5",
                rfcUrl: $"{RfcBase}#section-5.5"));

    public static WebSocketProtocolException InvalidPayloadLengthMsb() =>
        new(
            ProtocolViolationKind.InvalidPayloadLength,
            new FrameWrenchErrorDetail(
                "FW-PROTO-PAYLOAD-LEN-MSB",
                "64-bit payload length has the most-significant bit set",
                "The high bit of the 64-bit length field must be 0 per RFC 6455 §5.2.",
                suggestions: new[] { "Reject the connection; the peer sent an invalid frame header." },
                rfcSection: "RFC 6455 §5.2",
                rfcUrl: $"{RfcBase}#section-5.2"));

    public static WebSocketProtocolException PayloadLengthOverflow() =>
        new(
            ProtocolViolationKind.InvalidPayloadLength,
            new FrameWrenchErrorDetail(
                "FW-PROTO-PAYLOAD-LEN-OVERFLOW",
                "frame payload length exceeds Int32.MaxValue",
                "This client cannot buffer a single frame larger than 2 GiB.",
                suggestions: new[]
                {
                    "Lower the peer's maximum frame size.",
                    "Use application-level chunking with smaller frames.",
                },
                rfcSection: "RFC 6455 §5.2",
                rfcUrl: $"{RfcBase}#section-5.2"));

    public static WebSocketProtocolException PayloadTooLarge(long bytes, int limit, string limitName) =>
        new(
            ProtocolViolationKind.PayloadLimit,
            new FrameWrenchErrorDetail(
                "FW-PROTO-PAYLOAD-LIMIT",
                "frame or message payload exceeds the configured limit",
                $"Received {bytes:N0} bytes but {limitName} is {limit:N0} bytes.",
                ErrorContext.Create(
                    ("receivedBytes", bytes.ToString()),
                    ("limitBytes", limit.ToString()),
                    ("limitOption", limitName)),
                new[]
                {
                    $"Increase FrameWrenchOptions.{limitName} if the limit is too low for your workload.",
                    "Ensure the peer is not sending oversized frames due to a bug or attack.",
                },
                rfcSection: Rfc6455));

    public static WebSocketProtocolException InvalidCloseStatus(ushort code, bool inbound) =>
        new(
            ProtocolViolationKind.InvalidCloseStatus,
            new FrameWrenchErrorDetail(
                "FW-PROTO-INVALID-CLOSE-STATUS",
                $"invalid Close status code {code}",
                (inbound
                    ? "The peer sent a Close frame with a status code that must not appear on the wire."
                    : "The status code cannot be sent in a Close frame.") +
                " Codes 1005, 1006, and 1015 are reserved; codes below 1000 and undefined codes are invalid.",
                ErrorContext.Create(
                    ("statusCode", code.ToString()),
                    ("direction", inbound ? "inbound" : "outbound")),
                new[]
                {
                    "Use WireCloseStatus for CloseAsync and WebSocketFrame.Close.",
                    inbound
                        ? "Fail the connection; do not echo an invalid Close payload."
                        : "Choose a registered status from WireCloseStatus (e.g. NormalClosure, GoingAway).",
                },
                rfcSection: "RFC 6455 §7.4.1",
                rfcUrl: $"{RfcBase}#section-7.4.1"));

    public static WebSocketProtocolException CloseReasonTooLong(int encodedBytes) =>
        new(
            ProtocolViolationKind.InvalidClosePayload,
            new FrameWrenchErrorDetail(
                "FW-PROTO-CLOSE-REASON-LONG",
                "Close reason exceeds 123 UTF-8 bytes",
                $"The reason phrase encodes to {encodedBytes} bytes; at most 123 bytes are allowed after the 2-byte status code.",
                ErrorContext.Create(("encodedReasonBytes", encodedBytes.ToString())),
                new[] { "Shorten the reason string or omit it." },
                rfcSection: "RFC 6455 §7.4.1",
                rfcUrl: $"{RfcBase}#section-7.4.1"));

    public static WebSocketProtocolException InvalidUtf8(bool inbound, string payloadKind) =>
        new(
            ProtocolViolationKind.InvalidUtf8,
            new FrameWrenchErrorDetail(
                "FW-PROTO-INVALID-UTF8",
                $"invalid UTF-8 in {payloadKind}",
                (inbound
                    ? "Incoming Text data or Close reason is not well-formed UTF-8."
                    : "Outbound Text data is not well-formed UTF-8.") +
                " RFC 6455 requires failing the connection when validation is enabled.",
                ErrorContext.Create(
                    ("payloadKind", payloadKind),
                    ("direction", inbound ? "inbound" : "outbound")),
                new[]
                {
                    inbound
                        ? "Set FailOnInvalidIncomingUtf8 to false only for broken peers you validate yourself."
                        : "Ensure strings are valid Unicode before sending, or disable ValidateOutgoingMessages for raw frames.",
                    "Check for truncated multi-byte sequences at fragment boundaries.",
                },
                rfcSection: inbound ? "RFC 6455 §8.1" : "RFC 6455 §8.1",
                rfcUrl: $"{RfcBase}#section-8.1"));

    public static WebSocketProtocolException Fragmentation(
        string message,
        bool outbound,
        FrameOpCode? expected = null,
        FrameOpCode? actual = null)
    {
        var ctx = new List<(string, string)> { ("direction", outbound ? "outbound" : "inbound") };
        if (expected is not null) ctx.Add(("expectedOpcode", ErrorContext.FormatOpcode(expected.Value)));
        if (actual is not null) ctx.Add(("actualOpcode", ErrorContext.FormatOpcode(actual.Value)));

        return new WebSocketProtocolException(
            ProtocolViolationKind.Fragmentation,
            new FrameWrenchErrorDetail(
                "FW-PROTO-FRAGMENTATION",
                "message fragmentation sequence is invalid",
                message,
                ErrorContext.Create(ctx.ToArray()),
                new[]
                {
                    "Start a fragmented message with Text or Binary and FIN=false.",
                    "Send Continuation frames until the final fragment with FIN=true.",
                    "Do not interleave two data messages or send Continuation without a start frame.",
                },
                rfcSection: "RFC 6455 §5.4",
                rfcUrl: $"{RfcBase}#section-5.4"));
    }

    public static WebSocketHandshakeException HandshakeEmptyResponse() =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-EMPTY",
            "empty HTTP response during WebSocket handshake",
            "The server closed the connection or sent no bytes before the header block completed.",
            suggestions: new[]
            {
                "Verify host, port, and path (ws/wss URL).",
                "Check TLS certificates and proxy settings for wss://.",
            },
            operation: "ConnectAsync"));

    public static WebSocketHandshakeException HandshakeNon101(string statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-NON-101",
            "expected HTTP 101 Switching Protocols",
            $"The server returned: {ErrorContext.Truncate(statusLine, 120)}",
            ErrorContext.Create(("statusLine", ErrorContext.Truncate(statusLine))),
            new[]
            {
                "Confirm the endpoint supports WebSocket upgrade (not a REST-only URL).",
                "Read the HTTP status and body from server logs for 401/403/404/500 causes.",
            },
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeMissingUpgrade(string? upgrade, string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-UPGRADE",
            "missing or invalid Upgrade header",
            $"Expected 'Upgrade: websocket'; received '{ErrorContext.Truncate(upgrade)}'.",
            ErrorContext.Create(("upgrade", ErrorContext.Truncate(upgrade ?? "(missing)"))),
            new[] { "The response may not be a WebSocket handshake." },
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeMissingConnection(string? connection, string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-CONNECTION",
            "missing or invalid Connection header",
            $"Expected 'Connection' to include 'Upgrade'; received '{ErrorContext.Truncate(connection)}'.",
            ErrorContext.Create(("connection", ErrorContext.Truncate(connection ?? "(missing)"))),
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeAcceptMismatch(string expected, string? actual, string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-ACCEPT",
            "Sec-WebSocket-Accept mismatch",
            "The server's accept value does not match the hash of the client's Sec-WebSocket-Key. " +
            "This usually indicates a non-WebSocket server or a proxy altering the response.",
            ErrorContext.Create(
                ("expectedAccept", ErrorContext.Truncate(expected, 32) + "…"),
                ("receivedAccept", ErrorContext.Truncate(actual, 32))),
            new[]
            {
                "Verify you are connecting to a real WebSocket server.",
                "Check for intermediaries that terminate TLS and rewrite HTTP.",
            },
            rfcSection: "RFC 6455 §4.2.2",
            rfcUrl: $"{RfcBase}#section-4.2.2",
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeSubprotocolMultiple(string value, string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-SUBPROTO-MULTIPLE",
            "server returned multiple subprotocols",
            $"Sec-WebSocket-Protocol must be a single token; received '{value}'.",
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeSubprotocolUnadvertised(string token, string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-SUBPROTO-UNADVERTISED",
            "server selected a subprotocol the client did not offer",
            $"Server selected '{token}' but the client sent no Sec-WebSocket-Protocol header.",
            ErrorContext.Create(("selected", token)),
            new[] { "Add the subprotocol to FrameWrenchOptions.SubProtocols when connecting." },
            rfcSection: "RFC 6455 §4.1",
            rfcUrl: $"{RfcBase}#section-4.1",
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeSubprotocolMismatch(
        string token,
        IReadOnlyCollection<string> advertised,
        string? statusLine) =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-SUBPROTO-MISMATCH",
            "server selected an unadvertised subprotocol",
            $"Server selected '{token}' which is not in the client's offer list.",
            ErrorContext.Create(
                ("selected", token),
                ("advertised", string.Join(", ", advertised))),
            new[] { "Add the token to SubProtocols or fix the server configuration." },
            operation: "ConnectAsync"),
            statusLine);

    public static WebSocketHandshakeException HandshakeHeadersTooLarge() =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-HEADERS-LARGE",
            "HTTP response headers exceeded 16 KiB",
            "The handshake reader stopped to bound memory usage.",
            suggestions: new[] { "Inspect the server for excessive Set-Cookie or custom headers." },
            operation: "ConnectAsync"));

    public static WebSocketHandshakeException HandshakeConnectionClosed() =>
        new(new FrameWrenchErrorDetail(
            "FW-HANDSHAKE-CLOSED",
            "connection closed before handshake completed",
            "The TCP stream ended before the HTTP header terminator (CRLF CRLF) was received.",
            operation: "ConnectAsync"));

    public static ArgumentException HeaderInjection(string headerName, string reason) =>
        new(
            FrameWrenchErrorFormatter.Format(new FrameWrenchErrorDetail(
                "FW-HANDSHAKE-HEADER-INVALID",
                $"invalid HTTP header '{headerName}'",
                reason,
                ErrorContext.Create(("headerName", headerName)),
                new[]
                {
                    "Remove CR, LF, and NUL characters from header names and values.",
                    "Do not put Sec-WebSocket-Key, Upgrade, Connection, or Sec-WebSocket-Version in ExtraHeaders.",
                },
                operation: "FrameWrenchOptions")),
            headerName);

    public static ArgumentException ReservedHeaderOverride(string headerName) =>
        HeaderInjection(
            headerName,
            "This header is set automatically by FrameWrench and cannot be overridden via ExtraHeaders.");

    public static WebSocketStateException InvalidState(
        WebSocketState current,
        string operation,
        params WebSocketState[] allowed)
    {
        var allowedText = allowed.Length == 0
            ? "(none)"
            : string.Join(", ", allowed);

        return new WebSocketStateException(
            current,
            new FrameWrenchErrorDetail(
                "FW-STATE-INVALID",
                $"cannot call {operation} in state {current}",
                $"This operation requires state: {allowedText}.",
                ErrorContext.Create(
                    ("currentState", ErrorContext.FormatState(current)),
                    ("allowedStates", allowedText)),
                new[]
                {
                    current == WebSocketState.None
                        ? "Call ConnectAsync successfully before sending or receiving frames."
                        : "Create a new FrameWrenchClient to open another connection after close.",
                    "Await CloseAsync or DisposeAsync before reusing the instance.",
                },
                operation: operation));
    }

    public static WebSocketClosedByPeerException PeerClosed(
        CloseFrameInfo close,
        string consumerApi)
    {
        var statusPart = close.StatusCode is null
            ? "no status code"
            : close.Status is WireCloseStatus ws
                ? $"{ws} ({close.StatusCode})"
                : $"code {close.StatusCode} (non-standard)";

        var reasonPart = string.IsNullOrEmpty(close.Reason)
            ? string.Empty
            : $" Reason: {ErrorContext.Truncate(close.Reason)}";

        return new WebSocketClosedByPeerException(
            close,
            new FrameWrenchErrorDetail(
                "FW-PEER-CLOSED",
                "the server closed the WebSocket connection",
                $"Peer sent a Close frame while {consumerApi} was waiting for a data message ({statusPart}).{reasonPart}",
                ErrorContext.Create(
                    ("consumerApi", consumerApi),
                    ("statusCode", close.StatusCode?.ToString() ?? "(none)"),
                    ("closeReason", ErrorContext.Truncate(close.Reason))),
                new[]
                {
                    "Use ReceiveFramesAsync if you need to handle Close frames explicitly.",
                    "Inspect the close code and reason to decide whether to reconnect.",
                },
                operation: consumerApi));
    }

    public static FrameWrenchException ConnectionClosedNoFrames(WebSocketState state) =>
        new(new FrameWrenchErrorDetail(
            "FW-CONN-CLOSED",
            "no further frames are available",
            "The WebSocket connection ended before another frame could be read.",
            ErrorContext.Create(("connectionState", ErrorContext.FormatState(state))),
            new[]
            {
                "Check whether the peer sent Close or the TCP connection dropped.",
                "Read InnerException on the channel closed error for the root protocol failure.",
            }));

    public static FrameWrenchException TcpConnectFailed(string host, int port, Exception inner) =>
        new(
            new FrameWrenchErrorDetail(
                "FW-CONN-TCP",
                $"TCP connection to {host}:{port} failed",
                inner.Message,
                ErrorContext.Create(("host", host), ("port", port.ToString())),
                new[]
                {
                    "Verify host, port, firewall, and VPN settings.",
                    "For wss://, ensure TLS is available on the target port.",
                },
                operation: "ConnectAsync"),
            inner);

    public static ArgumentException InvalidUriScheme(string scheme) =>
        new(
            FrameWrenchErrorFormatter.Format(new FrameWrenchErrorDetail(
                "FW-ARG-URI-SCHEME",
                $"URI scheme must be ws or wss (got '{scheme}')",
                "FrameWrench only supports WebSocket schemes.",
                suggestions: new[] { "Use ws:// for cleartext or wss:// for TLS." })),
            nameof(Uri));

    public static ArgumentException ConnectCalledTwice() =>
        new(
            FrameWrenchErrorFormatter.Format(new FrameWrenchErrorDetail(
                "FW-STATE-CONNECT-ONCE",
                "ConnectAsync may only be called once per client instance",
                "Each FrameWrenchClient supports a single connection lifecycle.",
                suggestions: new[] { "Create a new FrameWrenchClient to reconnect." })));

    public static ArgumentException PingPayloadTooLarge() =>
        new(
            FrameWrenchErrorFormatter.Format(new FrameWrenchErrorDetail(
                "FW-ARG-PING-PAYLOAD",
                "Ping payload must not exceed 125 bytes",
                "RFC 6455 limits control frame payloads to 125 bytes.",
                rfcSection: "RFC 6455 §5.5",
                rfcUrl: $"{RfcBase}#section-5.5")));

    public static InvalidOperationException SingleFrameConsumerViolation() =>
        new(
            FrameWrenchErrorFormatter.Format(new FrameWrenchErrorDetail(
                "FW-STATE-SINGLE-CONSUMER",
                "only one frame consumer is allowed when SingleFrameConsumer is enabled",
                "A second call to ReceiveFramesAsync or ReceiveFrameAsync started while another is active.",
                suggestions: new[]
                {
                    "Use a single receive loop per connection.",
                    "Set SingleFrameConsumer to false if multiple readers are intentional.",
                })));
}
