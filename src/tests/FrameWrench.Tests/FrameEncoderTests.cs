using System.Buffers.Binary;
using FrameWrench.Core;
using FrameWrench.Protocol;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class FrameEncoderTests
{
    private static async Task<byte[]> EncodeAsync(WebSocketFrame frame, bool masked = false)
    {
        using var ms = new MemoryStream();
        await FrameEncoder.WriteAsync(ms, frame, masked: masked);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(FrameOpCode.Text, true, 0x81)]
    [InlineData(FrameOpCode.Binary, true, 0x82)]
    [InlineData(FrameOpCode.Close, true, 0x88)]
    [InlineData(FrameOpCode.Ping, true, 0x89)]
    [InlineData(FrameOpCode.Pong, true, 0x8A)]
    [InlineData(FrameOpCode.Continuation, false, 0x00)]
    [InlineData(FrameOpCode.Text, false, 0x01)]
    public async Task HeaderByte0_EncodesFINAndOpcode(FrameOpCode op, bool fin, byte expected)
    {
        var bytes = await EncodeAsync(new WebSocketFrame(op, fin, Array.Empty<byte>()));
        bytes[0].ShouldBe(expected,
            $"opcode {op} with FIN={fin} should produce byte0=0x{expected:X2}");
    }

    [Fact]
    public async Task Rsv1_SetsBit6()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Text, true, Array.Empty<byte>(), rsv1: true));
        (bytes[0] & 0x40).ShouldBe(0x40);
    }

    [Fact]
    public async Task Rsv2_SetsBit5()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Text, true, Array.Empty<byte>(), rsv2: true));
        (bytes[0] & 0x20).ShouldBe(0x20);
    }

    [Fact]
    public async Task Rsv3_SetsBit4()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Text, true, Array.Empty<byte>(), rsv3: true));
        (bytes[0] & 0x10).ShouldBe(0x10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(125)]
    public async Task SevenBitLength_EncodedInByte1(int payloadLen)
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[payloadLen]));

        (bytes[1] & 0x7F).ShouldBe(payloadLen);
        bytes.Length.ShouldBe(2 + payloadLen, "header(2) + payload");
    }

    [Theory]
    [InlineData(126)]
    [InlineData(1000)]
    [InlineData(65535)]
    public async Task SixteenBitLength_Len7Is126_PlusTwoExtensionBytes(int payloadLen)
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[payloadLen]));

        (bytes[1] & 0x7F).ShouldBe(126, "len7 must be 126 for 16-bit extended length");
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)).ShouldBe((ushort)payloadLen);
        bytes.Length.ShouldBe(2 + 2 + payloadLen);
    }

    [Fact]
    public async Task SixtyFourBitLength_Len7Is127_PlusEightExtensionBytes()
    {
        int payloadLen = 65536;
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[payloadLen]));

        (bytes[1] & 0x7F).ShouldBe(127, "len7 must be 127 for 64-bit extended length");
        BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(2, 8)).ShouldBe((ulong)payloadLen);
        bytes.Length.ShouldBe(2 + 8 + payloadLen);
    }

    [Fact]
    public async Task MaskedFrame_HasMaskBitSet_AndPayloadIsXoredWithKey()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Text, true, payload), masked: true);

        (bytes[1] & 0x80).ShouldBe(0x80, "MASK bit must be set");

        var maskKey = bytes.AsSpan(2, 4).ToArray();
        var rawPayload = bytes.AsSpan(6).ToArray();
        rawPayload.Length.ShouldBe(payload.Length);

        var decoded = new byte[payload.Length];
        for (int i = 0; i < payload.Length; i++)
            decoded[i] = (byte)(rawPayload[i] ^ maskKey[i & 3]);

        decoded.ShouldBe(payload);
    }

    [Fact]
    public async Task MaskedFrame_TotalLengthIs2Plus4PlusPayload()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[10]), masked: true);
        bytes.Length.ShouldBe(2 + 4 + 10, "header(2) + mask(4) + payload(10)");
    }

    [Fact]
    public async Task UnmaskedFrame_MaskBitClear_PayloadVerbatim()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, payload), masked: false);

        (bytes[1] & 0x80).ShouldBe(0, "MASK bit must be clear");
        bytes.AsSpan(2).ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task ControlFrame_PayloadOver125Bytes_Throws()
    {
        var frame = new WebSocketFrame(FrameOpCode.Ping, true, new byte[126]);
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            async () => await EncodeAsync(frame));
        ex.ErrorCode.ShouldBe("FW-PROTO-CONTROL-PAYLOAD-LARGE");
    }

    [Fact]
    public async Task ControlFrame_FragmentedFINFalse_Throws()
    {
        var frame = new WebSocketFrame(FrameOpCode.Ping, isFinal: false, Array.Empty<byte>());
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            async () => await EncodeAsync(frame));
        ex.ErrorCode.ShouldBe("FW-PROTO-FRAGMENTED-CONTROL");
    }

    [Fact]
    public async Task CloseFrame_EncodesStatusAndReason()
    {
        var bytes = await EncodeAsync(WebSocketFrame.Close(WireCloseStatus.GoingAway, "bye"));
        bytes[0].ShouldBe((byte)0x88);
        var payloadLen = bytes[1] & 0x7F;
        payloadLen.ShouldBeGreaterThan(2);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2))
            .ShouldBe((ushort)WireCloseStatus.GoingAway);
    }

    [Fact]
    public async Task CloseFrame_NoReason_TotalLengthIsHeaderPlusTwoBytes()
    {
        var bytes = await EncodeAsync(WebSocketFrame.Close(WireCloseStatus.NormalClosure));
        bytes.Length.ShouldBe(2 + 2, "header(2) + status(2) only when no reason supplied");
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2))
            .ShouldBe((ushort)WireCloseStatus.NormalClosure);
    }

    [Fact]
    public async Task PingFrame_EmptyPayload_CorrectOpcode()
    {
        var bytes = await EncodeAsync(WebSocketFrame.Ping());
        bytes[0].ShouldBe((byte)0x89, "FIN(1) + Ping opcode(0x9)");
        (bytes[1] & 0x7F).ShouldBe(0, "empty payload");
    }

    [Fact]
    public async Task PongFrame_EmptyPayload_CorrectOpcode()
    {
        var bytes = await EncodeAsync(WebSocketFrame.Pong());
        bytes[0].ShouldBe((byte)0x8A, "FIN(1) + Pong opcode(0xA)");
        (bytes[1] & 0x7F).ShouldBe(0, "empty payload");
    }

    [Fact]
    public async Task PingFrame_WithPayload_PayloadRoundTrips()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var bytes = await EncodeAsync(WebSocketFrame.Ping(payload), masked: false);
        bytes.AsSpan(2).ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task MaskedFrame_With16BitLength_HasMaskBitAndCorrectLength()
    {
        const int payloadLen = 200;
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[payloadLen]), masked: true);

        (bytes[1] & 0x80).ShouldBe(0x80, "MASK bit must be set");
        (bytes[1] & 0x7F).ShouldBe(126, "len7 must be 126 for 16-bit length");
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)).ShouldBe((ushort)payloadLen);
        bytes.Length.ShouldBe(2 + 2 + 4 + payloadLen, "header(2) + len16(2) + mask(4) + payload");
    }

    [Fact]
    public async Task MaskedFrame_With64BitLength_HasMaskBitAndCorrectLength()
    {
        const int payloadLen = 65536;
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, new byte[payloadLen]), masked: true);

        (bytes[1] & 0x80).ShouldBe(0x80, "MASK bit must be set");
        (bytes[1] & 0x7F).ShouldBe(127, "len7 must be 127 for 64-bit length");
        BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(2, 8)).ShouldBe((ulong)payloadLen);
        bytes.Length.ShouldBe(2 + 8 + 4 + payloadLen, "header(2) + len64(8) + mask(4) + payload");
    }

    [Fact]
    public async Task AllRsvBitsSet_EncodesCorrectly()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Text, true, Array.Empty<byte>(),
                rsv1: true, rsv2: true, rsv3: true));

        (bytes[0] & 0x70).ShouldBe(0x70, "RSV1+RSV2+RSV3 bits must all be set");
    }

    [Fact]
    public async Task ContinuationFrame_NotFinal_EncodesAsFinalFalse()
    {
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Continuation, isFinal: false, new byte[4]));

        (bytes[0] & 0x80).ShouldBe(0, "FIN bit must be clear");
        (bytes[0] & 0x0F).ShouldBe(0, "Continuation opcode must be 0x0");
    }

    [Fact]
    public async Task MaskedFrame_XorWithKey_EachByteUsesCorrectKeyOffset()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var bytes = await EncodeAsync(
            new WebSocketFrame(FrameOpCode.Binary, true, payload), masked: true);

        var maskKey = bytes.AsSpan(2, 4).ToArray();
        var masked = bytes.AsSpan(6).ToArray();

        for (var i = 0; i < payload.Length; i++)
            ((byte)(masked[i] ^ maskKey[i & 3])).ShouldBe(payload[i],
                $"byte at index {i} did not round-trip through XOR masking");
    }
}
