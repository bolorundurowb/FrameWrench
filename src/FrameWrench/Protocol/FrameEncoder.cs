using System.Buffers;
using System.Buffers.Binary;
using FrameWrench.Core;

namespace FrameWrench.Protocol;

/// <summary>
/// Encodes a <see cref="WebSocketFrame"/> into its RFC 6455 wire format and writes
/// it to a <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// All frames sent by a client MUST be masked (RFC 6455 §5.3).
/// FrameWrench generates a cryptographically-random 4-byte masking key for
/// every frame.
/// </para>
/// <para>
/// Wire format (variable-length header + payload):
/// <code>
///  Byte 0:  FIN(1) RSV1(1) RSV2(1) RSV3(1) Opcode(4)
///  Byte 1:  MASK(1) PayloadLen(7)         [0–125]
///         or MASK(1) 126(7) + 16-bit len  [126–65535]
///         or MASK(1) 127(7) + 64-bit len  [&gt;65535]
///  [4 bytes] Masking key (if MASK=1)
///  [N bytes] Masked payload
/// </code>
/// </para>
/// </remarks>
internal static class FrameEncoder
{
    /// <summary>
    /// Encodes <paramref name="frame"/> and writes it to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The destination stream (connected TCP or TLS stream).</param>
    /// <param name="frame">The frame to encode.</param>
    /// <param name="masked">
    /// <c>true</c> (default) to apply client masking as required by RFC 6455 §5.3.
    /// Pass <c>false</c> only for test/server scenarios.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task WriteAsync(
        Stream            stream,
        WebSocketFrame    frame,
        bool              masked = true,
        CancellationToken ct     = default)
    {
        var payloadLen = frame.Payload.Length;

        if (frame.IsControl && payloadLen > 125)
            throw new Core.WebSocketProtocolException(
                $"Control frame payload may not exceed 125 bytes (was {payloadLen}).");

        if (frame.IsControl && !frame.IsFinal)
            throw new Core.WebSocketProtocolException(
                "Control frames must have the FIN bit set (no fragmentation allowed).");

        byte byte0 = (byte)(
            (frame.IsFinal ? 0x80 : 0x00) |
            (frame.Rsv1   ? 0x40 : 0x00) |
            (frame.Rsv2   ? 0x20 : 0x00) |
            (frame.Rsv3   ? 0x10 : 0x00) |
            ((byte)frame.OpCode & 0x0F));

        byte maskBit = masked ? (byte)0x80 : (byte)0x00;

        int  headerSize;
        byte lenByte;

        if (payloadLen <= 125)
        {
            headerSize = 2;
            lenByte    = (byte)payloadLen;
        }
        else if (payloadLen <= 65535)
        {
            headerSize = 4;
            lenByte    = 126;
        }
        else
        {
            headerSize = 10;
            lenByte    = 127;
        }

        if (masked) headerSize += 4;

        int  totalSize  = headerSize + payloadLen;
        var  rentedBuf  = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            int pos = 0;
            rentedBuf[pos++] = byte0;
            rentedBuf[pos++] = (byte)(maskBit | lenByte);

            if (lenByte == 126)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    rentedBuf.AsSpan(pos, 2), (ushort)payloadLen);
                pos += 2;
            }
            else if (lenByte == 127)
            {
                BinaryPrimitives.WriteUInt64BigEndian(
                    rentedBuf.AsSpan(pos, 8), (ulong)payloadLen);
                pos += 8;
            }

            if (masked)
            {
                var maskKey = GenerateMaskKey();
                rentedBuf[pos]     = maskKey[0];
                rentedBuf[pos + 1] = maskKey[1];
                rentedBuf[pos + 2] = maskKey[2];
                rentedBuf[pos + 3] = maskKey[3];
                pos += 4;

                var src = frame.Payload.Span;
                for (int i = 0; i < payloadLen; i++)
                    rentedBuf[pos + i] = (byte)(src[i] ^ maskKey[i & 3]);
            }
            else
            {
                frame.Payload.Span.CopyTo(rentedBuf.AsSpan(pos));
            }

            await stream.WriteAsync(rentedBuf, 0, totalSize, ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuf);
        }
    }

    /// <summary>
    /// Generates a cryptographically random 4-byte masking key.
    /// </summary>
    private static byte[] GenerateMaskKey()
    {
        var key = new byte[4];
#if NETFRAMEWORK || NETSTANDARD2_0
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            rng.GetBytes(key);
#else
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
#endif
        return key;
    }
}
