using System.Security.Cryptography;
using System.Text;
using FrameWrench.Core;

namespace FrameWrench.Protocol;

/// <summary>
/// Builds and validates the RFC 6455 HTTP Upgrade handshake.
/// </summary>
/// <remarks>
/// <para>
/// The client handshake is an HTTP/1.1 GET request with the following mandatory headers:
/// <list type="bullet">
///   <item><c>Upgrade: websocket</c></item>
///   <item><c>Connection: Upgrade</c></item>
///   <item><c>Sec-WebSocket-Key: &lt;base64-encoded 16-byte nonce&gt;</c></item>
///   <item><c>Sec-WebSocket-Version: 13</c></item>
/// </list>
/// </para>
/// <para>
/// The server must respond with <c>101 Switching Protocols</c> and a
/// <c>Sec-WebSocket-Accept</c> header equal to
/// <c>Base64(SHA-1(Sec-WebSocket-Key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))</c>.
/// </para>
/// </remarks>
internal static class HandshakeHelper
{
    /// <summary>The GUID appended to the client key before SHA-1 hashing (RFC 6455 §1.3).</summary>
    private const string Rfc6455Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>
    /// Generates a cryptographically-random 16-byte nonce and returns it both
    /// as the raw bytes (for accept validation) and as a Base64 string (for the header).
    /// </summary>
    public static (byte[] keyBytes, string keyBase64) GenerateKey()
    {
        var bytes = new byte[16];
#if NETFRAMEWORK || NETSTANDARD2_0
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
#else
        RandomNumberGenerator.Fill(bytes);
#endif
        return (bytes, Convert.ToBase64String(bytes));
    }

    /// <summary>
    /// Computes the expected <c>Sec-WebSocket-Accept</c> header value for the given key.
    /// </summary>
    /// <param name="keyBase64">The Base64-encoded 16-byte nonce from <c>Sec-WebSocket-Key</c>.</param>
    public static string ComputeAcceptValue(string keyBase64)
    {
        var combined = keyBase64 + Rfc6455Guid;
        var bytes    = Encoding.ASCII.GetBytes(combined);
#if NETFRAMEWORK || NETSTANDARD2_0
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(bytes);
#else
        var hash = SHA1.HashData(bytes);
#endif
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Builds the full HTTP Upgrade request bytes ready to write to the stream.
    /// </summary>
    /// <param name="uri">The WebSocket URI (<c>ws://</c> or <c>wss://</c>).</param>
    /// <param name="keyBase64">The Base64-encoded nonce from <see cref="GenerateKey"/>.</param>
    /// <param name="extraHeaders">
    /// Optional additional headers (e.g., <c>Authorization</c>, <c>Origin</c>).
    /// Each entry is written as <c>key: value\r\n</c>.
    /// </param>
    public static byte[] BuildRequest(
        Uri                                  uri,
        string                               keyBase64,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var host   = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var path   = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

        var sb = new StringBuilder();
        sb.Append($"GET {path} HTTP/1.1\r\n");
        sb.Append($"Host: {host}\r\n");
        sb.Append("Upgrade: websocket\r\n");
        sb.Append("Connection: Upgrade\r\n");
        sb.Append($"Sec-WebSocket-Key: {keyBase64}\r\n");
        sb.Append("Sec-WebSocket-Version: 13\r\n");

        if (extraHeaders != null)
            foreach (var kv in extraHeaders)
                sb.Append($"{kv.Key}: {kv.Value}\r\n");

        sb.Append("\r\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Reads the HTTP response from the stream and validates it as a successful
    /// 101 Switching Protocols response with a correct <c>Sec-WebSocket-Accept</c> value.
    /// </summary>
    /// <param name="stream">The raw (or TLS) stream connected to the server.</param>
    /// <param name="expectedAccept">
    /// The expected value of the <c>Sec-WebSocket-Accept</c> header, as computed by
    /// <see cref="ComputeAcceptValue"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="WebSocketHandshakeException">
    /// Thrown if the response is not a valid 101 upgrade or the accept value is wrong.
    /// </exception>
    public static async Task ValidateResponseAsync(
        Stream            stream,
        string            expectedAccept,
        CancellationToken ct)
    {
        var headerBytes = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
        var response    = Encoding.ASCII.GetString(headerBytes);
        var lines       = response.Split(new[] { "\r\n" }, StringSplitOptions.None);

        if (lines.Length == 0)
            throw new WebSocketHandshakeException("Empty response from server.", statusLine: null);

        var statusLine = lines[0];

        if (!statusLine.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase) &&
            !statusLine.StartsWith("HTTP/1.0 101", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebSocketHandshakeException(
                $"Expected 101 Switching Protocols but received: {statusLine}",
                statusLine);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var line  = lines[i];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key   = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            headers[key] = value;
        }

        if (!headers.TryGetValue("Upgrade", out var upgrade) ||
            !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            throw new WebSocketHandshakeException(
                $"Missing or invalid 'Upgrade' header: '{upgrade}'.", statusLine);
        }

        if (!headers.TryGetValue("Connection", out var connection) ||
            connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new WebSocketHandshakeException(
                $"Missing or invalid 'Connection' header: '{connection}'.", statusLine);
        }

        if (!headers.TryGetValue("Sec-WebSocket-Accept", out var accept) ||
            !string.Equals(accept, expectedAccept, StringComparison.Ordinal))
        {
            throw new WebSocketHandshakeException(
                $"Sec-WebSocket-Accept mismatch. Expected '{expectedAccept}', got '{accept}'.",
                statusLine);
        }
    }

    /// <summary>
    /// Reads raw bytes from the stream until the HTTP header terminator
    /// (<c>\r\n\r\n</c>) is found, then returns everything up to and including
    /// that terminator.  Does not read any bytes beyond the header block.
    /// </summary>
    private static async Task<byte[]> ReadHttpHeadersAsync(
        Stream            stream,
        CancellationToken ct)
    {
        var buffer = new List<byte>(512);
        var window = new byte[4];

        var singleByte = new byte[1];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            int n = await stream
                .ReadAsync(singleByte, 0, 1, ct)
                .ConfigureAwait(false);

            if (n == 0)
                throw new WebSocketHandshakeException(
                    "Connection closed before the HTTP handshake completed.");

            buffer.Add(singleByte[0]);

            window[0] = window[1];
            window[1] = window[2];
            window[2] = window[3];
            window[3] = singleByte[0];

            if (window[0] == '\r' && window[1] == '\n' &&
                window[2] == '\r' && window[3] == '\n')
                break;

            if (buffer.Count > 16 * 1024)
                throw new WebSocketHandshakeException(
                    "HTTP response headers exceeded the 16 KiB limit.");
        }

        return buffer.ToArray();
    }
}
