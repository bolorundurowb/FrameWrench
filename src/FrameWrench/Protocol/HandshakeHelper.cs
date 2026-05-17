using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using FrameWrench.Internal;

namespace FrameWrench.Protocol;

internal static class HandshakeHelper
{
    private const string Rfc6455Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static (byte[] keyBytes, string keyBase64) GenerateKey()
    {
        var bytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);

        return (bytes, Convert.ToBase64String(bytes));
    }

    public static string ComputeAcceptValue(string keyBase64)
    {
        var combined = keyBase64 + Rfc6455Guid;
        var bytes = Encoding.ASCII.GetBytes(combined);
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public static byte[] BuildRequest(
        Uri uri,
        string keyBase64,
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;

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

    public static async Task<string?> ValidateResponseAsync(
        Stream stream,
        string expectedAccept,
        CancellationToken ct,
        IReadOnlyCollection<string>? advertisedSubProtocols = null)
    {
        var headerBytes = await ReadHttpHeadersAsync(stream, ct).ConfigureAwait(false);
        var response = Encoding.ASCII.GetString(headerBytes);
        var lines = response.Split(["\r\n"], StringSplitOptions.None);

        if (lines.Length == 0)
            throw FrameWrenchErrors.HandshakeEmptyResponse();

        var statusLine = lines[0];

        if (!statusLine.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase) &&
            !statusLine.StartsWith("HTTP/1.0 101", StringComparison.OrdinalIgnoreCase))
            throw FrameWrenchErrors.HandshakeNon101(statusLine);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            headers[key] = value;
        }

        headers.TryGetValue("Upgrade", out var upgrade);
        if (!upgrade?.Equals("websocket", StringComparison.OrdinalIgnoreCase) ?? true)
            throw FrameWrenchErrors.HandshakeMissingUpgrade(upgrade, statusLine);

        headers.TryGetValue("Connection", out var connection);
        if (connection is null || connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0)
            throw FrameWrenchErrors.HandshakeMissingConnection(connection, statusLine);

        headers.TryGetValue("Sec-WebSocket-Accept", out var accept);
        if (!string.Equals(accept, expectedAccept, StringComparison.Ordinal))
            throw FrameWrenchErrors.HandshakeAcceptMismatch(expectedAccept, accept, statusLine);

        var selectedSubProtocol = headers.TryGetValue("Sec-WebSocket-Protocol", out var protoHeader)
            ? protoHeader
            : null;

        if (!string.IsNullOrEmpty(selectedSubProtocol))
        {
            var token = selectedSubProtocol!.Trim();

            if (token.IndexOf(',') >= 0)
                throw FrameWrenchErrors.HandshakeSubprotocolMultiple(selectedSubProtocol, statusLine);

            if (advertisedSubProtocols is null || advertisedSubProtocols.Count == 0)
                throw FrameWrenchErrors.HandshakeSubprotocolUnadvertised(token, statusLine);

            var matched = false;
            foreach (var advertised in advertisedSubProtocols)
            {
                if (string.Equals(advertised, token, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
                throw FrameWrenchErrors.HandshakeSubprotocolMismatch(token, advertisedSubProtocols, statusLine);

            return token;
        }

        return null;
    }

    private static async Task<byte[]> ReadHttpHeadersAsync(Stream stream, CancellationToken ct)
    {
        const int maxHeaders = 16 * 1024;
        const int readChunk = 1024;

        var pool = ArrayPool<byte>.Shared;
        var buf = pool.Rent(maxHeaders + readChunk);

        try
        {
            var total = 0;
            while (total <= maxHeaders)
            {
                ct.ThrowIfCancellationRequested();

                var room = buf.Length - total;
                if (room == 0)
                    throw FrameWrenchErrors.HandshakeHeadersTooLarge();

                var toRead = Math.Min(readChunk, room);
                var n = await stream.ReadAsync(buf, total, toRead, ct).ConfigureAwait(false);
                if (n == 0)
                    throw FrameWrenchErrors.HandshakeConnectionClosed();

                total += n;

                if (total > maxHeaders)
                    throw FrameWrenchErrors.HandshakeHeadersTooLarge();

                var end = FindHeaderBlockEnd(buf, total);
                if (end >= 0)
                {
                    var result = new byte[end];
                    Buffer.BlockCopy(buf, 0, result, 0, end);
                    return result;
                }
            }

            throw FrameWrenchErrors.HandshakeHeadersTooLarge();
        }
        finally
        {
            pool.Return(buf);
        }
    }

    private static int FindHeaderBlockEnd(byte[] buffer, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
                buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                return i + 4;
        }

        return -1;
    }
}
