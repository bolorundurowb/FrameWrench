using System.Buffers.Binary;
using FrameWrench.Core;

namespace FrameWrench.Protocol;

/// <summary>
/// Reads raw bytes from a <see cref="Stream"/> and parses them into
/// <see cref="WebSocketFrame"/> instances following RFC 6455 §5.2.
/// </summary>
/// <remarks>
/// <para>
/// The decoder assumes server-to-client framing.  Per RFC 6455 §5.1, a server
/// MUST NOT mask frames sent to a client; masked server frames are treated as a
/// protocol error.
/// </para>
/// <para>
/// Callers should issue <see cref="ReadFrameAsync"/> sequentially from a single
/// reader loop to avoid interleaved reads on the underlying stream.
/// </para>
/// </remarks>
internal static class FrameDecoder
{
    /// <summary>
    /// Asynchronously reads the next WebSocket frame from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The source stream positioned at the start of a frame.</param>
    /// <param name="maxPayloadBytes">
    /// Maximum accepted payload length in bytes (default: 64 MiB).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fully decoded, unmasked frame.</returns>
    /// <exception cref="WebSocketProtocolException">
    /// Thrown on any RFC 6455 protocol violation detected during decoding.
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// Thrown if the connection closes unexpectedly while reading a frame.
    /// </exception>
    public static async Task<WebSocketFrame> ReadFrameAsync(
        Stream stream,
        int maxPayloadBytes = 64 * 1024 * 1024,
        CancellationToken ct = default)
    {
        var header = new byte[2];
        await ReadExactAsync(stream, header, 0, 2, ct).ConfigureAwait(false);

        byte byte0 = header[0];
        byte byte1 = header[1];

        bool fin = (byte0 & 0x80) != 0;
        bool rsv1 = (byte0 & 0x40) != 0;
        bool rsv2 = (byte0 & 0x20) != 0;
        bool rsv3 = (byte0 & 0x10) != 0;
        var opCode = (FrameOpCode)(byte0 & 0x0F);

        bool masked = (byte1 & 0x80) != 0;
        int len7 = byte1 & 0x7F;

        if (masked)
            throw new WebSocketProtocolException(
                "Received a masked frame from the server. " +
                "Per RFC 6455 §5.1, servers must not mask frames sent to a client.");

        // RFC 6455 §5.2: RSV1–RSV3 MUST be 0 unless defined by a negotiated extension.
        // FrameWrench does not negotiate any per-message extensions, so any non-zero RSV
        // bit is a protocol error.
        if (rsv1 || rsv2 || rsv3)
            throw new WebSocketProtocolException(
                $"Received frame with non-zero RSV bits (RSV1={rsv1}, RSV2={rsv2}, RSV3={rsv3}). " +
                "RFC 6455 §5.2 requires RSV bits to be 0 unless a negotiated extension defines their use.");

        ValidateOpCode(opCode, fin);

        long payloadLen;

        if (len7 <= 125)
        {
            payloadLen = len7;
        }
        else if (len7 == 126)
        {
            var extBuf = new byte[2];
            await ReadExactAsync(stream, extBuf, 0, 2, ct).ConfigureAwait(false);
            payloadLen = BinaryPrimitives.ReadUInt16BigEndian(extBuf);
        }
        else // len7 == 127
        {
            var extBuf = new byte[8];
            await ReadExactAsync(stream, extBuf, 0, 8, ct).ConfigureAwait(false);
            ulong raw = BinaryPrimitives.ReadUInt64BigEndian(extBuf);

            if ((raw & 0x8000_0000_0000_0000UL) != 0)
                throw new WebSocketProtocolException(
                    "64-bit payload length has the most-significant bit set, which is prohibited " +
                    "by RFC 6455 §5.2.");

            payloadLen = (long)raw;
        }

        if (IsControlOpCode(opCode) && payloadLen > 125)
            throw new WebSocketProtocolException(
                $"Control frame payload exceeds 125 bytes (received {payloadLen} bytes). " +
                "RFC 6455 §5.5 prohibits this.");

        if (payloadLen > maxPayloadBytes)
            throw new WebSocketProtocolException(
                $"Frame payload ({payloadLen:N0} bytes) exceeds the configured maximum of " +
                $"{maxPayloadBytes:N0} bytes.");

        var payload = await ReadPayloadAsync(stream, (int)payloadLen, ct)
            .ConfigureAwait(false);

        return new WebSocketFrame(opCode, fin, payload, rsv1, rsv2, rsv3);
    }

    /// <summary>
    /// Reads exactly <paramref name="length"/> bytes into a precisely-sized owned array.
    /// </summary>
    private static async Task<byte[]> ReadPayloadAsync(
        Stream stream,
        int length,
        CancellationToken ct)
    {
        if (length == 0) return [];

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, 0, length, ct).ConfigureAwait(false);
        return payload;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes into <paramref name="buffer"/>
    /// at <paramref name="offset"/>.  Throws <see cref="EndOfStreamException"/> on EOF.
    /// </summary>
    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            ct.ThrowIfCancellationRequested();

            int n = await stream
                .ReadAsync(buffer, offset + totalRead, count - totalRead, ct)
                .ConfigureAwait(false);

            if (n == 0)
                throw new EndOfStreamException(
                    $"The connection closed mid-frame (expected {count} bytes, read {totalRead}).");

            totalRead += n;
        }
    }

    private static void ValidateOpCode(FrameOpCode opCode, bool fin)
    {
        byte raw = (byte)opCode;

        if ((raw >= 0x3 && raw <= 0x7) || (raw >= 0xB && raw <= 0xF))
            throw new WebSocketProtocolException(
                $"Received frame with reserved opcode 0x{raw:X1}. " +
                "Reserved opcodes require prior extension negotiation.");

        if (IsControlOpCode(opCode) && !fin)
            throw new WebSocketProtocolException(
                $"Control frame (opcode {opCode}) has the FIN bit cleared. " +
                "RFC 6455 §5.5 prohibits fragmented control frames.");
    }

    private static bool IsControlOpCode(FrameOpCode op) =>
        op is FrameOpCode.Close or FrameOpCode.Ping or FrameOpCode.Pong;
}
