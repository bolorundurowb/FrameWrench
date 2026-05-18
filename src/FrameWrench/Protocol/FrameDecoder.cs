using System.Buffers.Binary;
using FrameWrench.Core;
using FrameWrench.Internal;

namespace FrameWrench.Protocol;

internal static class FrameDecoder
{
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
            throw FrameWrenchErrors.MaskedServerFrame(WebSocketState.Open);

        if (rsv1 || rsv2 || rsv3)
            throw FrameWrenchErrors.NonZeroRsv(rsv1, rsv2, rsv3);

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
        else
        {
            var extBuf = new byte[8];
            await ReadExactAsync(stream, extBuf, 0, 8, ct).ConfigureAwait(false);
            ulong raw = BinaryPrimitives.ReadUInt64BigEndian(extBuf);

            if ((raw & 0x8000_0000_0000_0000UL) != 0)
                throw FrameWrenchErrors.InvalidPayloadLengthMsb();

            payloadLen = (long)raw;
        }

        if (IsControlOpCode(opCode) && payloadLen > 125)
            throw FrameWrenchErrors.ControlPayloadTooLarge(payloadLen);

        if (payloadLen > int.MaxValue)
            throw FrameWrenchErrors.PayloadLengthOverflow();

        if (payloadLen > maxPayloadBytes)
            throw FrameWrenchErrors.PayloadTooLarge(payloadLen, maxPayloadBytes, nameof(FrameWrenchOptions.MaxFramePayloadBytes));

        var payload = await ReadPayloadAsync(stream, (int)payloadLen, ct)
            .ConfigureAwait(false);

        return new WebSocketFrame(opCode, fin, payload, rsv1, rsv2, rsv3);
    }

    private static async Task<byte[]> ReadPayloadAsync(
        Stream stream,
        int length,
        CancellationToken ct)
    {
        if (length == 0)
            return [];

        var result = new byte[length];
        await ReadExactAsync(stream, result, 0, length, ct).ConfigureAwait(false);
        return result;
    }

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
            throw FrameWrenchErrors.ReservedOpcode(raw, fin);

        if (IsControlOpCode(opCode) && !fin)
            throw FrameWrenchErrors.FragmentedControlFrame(opCode);
    }

    private static bool IsControlOpCode(FrameOpCode op) =>
        op is FrameOpCode.Close or FrameOpCode.Ping or FrameOpCode.Pong;
}
