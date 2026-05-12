using System.Buffers.Binary;
using FrameWrench.Core;
using FrameWrench.Protocol;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class FrameDecoderTests
{
    private static byte[] BuildServerFrame(
        FrameOpCode opCode,
        bool fin,
        byte[] payload,
        bool masked = false,
        bool rsv1 = false,
        bool rsv2 = false,
        bool rsv3 = false)
    {
        using var ms = new MemoryStream();

        byte byte0 =
            (byte)((fin ? 0x80 : 0) |
                   (rsv1 ? 0x40 : 0) |
                   (rsv2 ? 0x20 : 0) |
                   (rsv3 ? 0x10 : 0) |
                   ((byte)opCode & 0x0F));
        byte maskBit = masked ? (byte)0x80 : (byte)0;

        ms.WriteByte(byte0);

        if (payload.Length <= 125)
        {
            ms.WriteByte((byte)(maskBit | payload.Length));
        }
        else if (payload.Length <= 65535)
        {
            ms.WriteByte((byte)(maskBit | 126));
            var ext = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(ext, (ushort)payload.Length);
            ms.Write(ext, 0, 2);
        }
        else
        {
            ms.WriteByte((byte)(maskBit | 127));
            var ext = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(ext, (ulong)payload.Length);
            ms.Write(ext, 0, 8);
        }

        ms.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    private static Task<WebSocketFrame> Decode(byte[] wire) =>
        FrameDecoder.ReadFrameAsync(new MemoryStream(wire));

    [Theory]
    [InlineData(FrameOpCode.Text)]
    [InlineData(FrameOpCode.Binary)]
    [InlineData(FrameOpCode.Continuation)]
    [InlineData(FrameOpCode.Close)]
    [InlineData(FrameOpCode.Ping)]
    [InlineData(FrameOpCode.Pong)]
    public async Task Decode_AllValidOpcodes_ParsesCorrectly(FrameOpCode opCode)
    {
        bool fin = opCode is FrameOpCode.Close or FrameOpCode.Ping or FrameOpCode.Pong;
        var payload = new byte[] { 0x42, 0x43 };
        var frame = await Decode(BuildServerFrame(opCode, fin, payload));

        frame.OpCode.ShouldBe(opCode);
        frame.IsFinal.ShouldBe(fin);
        frame.Payload.ToArray().ShouldBe(payload);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Decode_FINBit_ParsedCorrectly(bool fin)
    {
        var frame = await Decode(BuildServerFrame(FrameOpCode.Text, fin, new byte[1]));
        frame.IsFinal.ShouldBe(fin);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Decode_NonZeroRsv_Throws_ProtocolException(bool rsv1, bool rsv2, bool rsv3)
    {
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => Decode(BuildServerFrame(
                FrameOpCode.Text,
                true,
                [],
                rsv1: rsv1,
                rsv2: rsv2,
                rsv3: rsv3)));
        ex.Message.ShouldContain("RSV");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(125)]
    public async Task SevenBitLength_DecodesCorrectly(int len)
    {
        var payload = new byte[len];
        new Random(42).NextBytes(payload);
        var frame = await Decode(BuildServerFrame(FrameOpCode.Binary, true, payload));
        frame.Payload.Length.ShouldBe(len);
        frame.Payload.ToArray().ShouldBe(payload);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(256)]
    [InlineData(65535)]
    public async Task SixteenBitLength_DecodesCorrectly(int len)
    {
        var frame = await Decode(BuildServerFrame(FrameOpCode.Binary, true, new byte[len]));
        frame.Payload.Length.ShouldBe(len);
    }

    [Fact]
    public async Task SixtyFourBitLength_DecodesCorrectly()
    {
        var frame = await Decode(BuildServerFrame(FrameOpCode.Binary, true, new byte[65536]));
        frame.Payload.Length.ShouldBe(65536);
    }

    [Fact]
    public async Task CloseFrame_ExtractsStatusAndReason()
    {
        const string reason = "test close";
        var reasonBytes = System.Text.Encoding.UTF8.GetBytes(reason);
        var payload = new byte[2 + reasonBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)WebSocketCloseStatus.GoingAway);
        reasonBytes.CopyTo(payload, 2);

        var frame = await Decode(BuildServerFrame(FrameOpCode.Close, true, payload));
        frame.GetCloseData(out var status, out var parsed);

        status.ShouldBe(WebSocketCloseStatus.GoingAway);
        parsed.ShouldBe(reason);
    }

    [Fact]
    public async Task CloseFrame_EmptyPayload_ReturnsNullStatus()
    {
        var frame = await Decode(BuildServerFrame(FrameOpCode.Close, true, []));
        frame.GetCloseData(out var status, out var reason);
        status.ShouldBeNull();
        reason.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaskedServerFrame_Throws_ProtocolException()
    {
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => Decode(BuildServerFrame(FrameOpCode.Text, true, new byte[4], masked: true)));
        ex.Message.ShouldContain("servers must not mask");
    }

    [Theory]
    [InlineData(0x3)]
    [InlineData(0x5)]
    [InlineData(0x7)]
    [InlineData(0xB)]
    [InlineData(0xF)]
    public async Task ReservedOpCode_Throws_ProtocolException(byte reserved)
    {
        var wire = new byte[] { (byte)(0x80 | reserved), 0x00 };
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(new MemoryStream(wire)));
        ex.Message.ShouldContain("reserved opcode");
    }

    [Fact]
    public async Task FragmentedControlFrame_Throws_ProtocolException()
    {
        var wire = new byte[] { 0x09, 0x00 };
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(new MemoryStream(wire)));
        ex.Message.ShouldContain("fragmented");
    }

    [Fact]
    public async Task ControlFrame_PayloadOver125_Throws_ProtocolException()
    {
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => Decode(BuildServerFrame(FrameOpCode.Ping, true, new byte[126])));
        ex.Message.ShouldContain("125");
    }

    [Fact]
    public async Task MaxPayloadExceeded_Throws_ProtocolException()
    {
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(
                new MemoryStream(BuildServerFrame(FrameOpCode.Binary, true, new byte[1000])),
                maxPayloadBytes: 100));
        ex.Message.ShouldContain("exceeds the configured maximum");
    }

    [Fact]
    public async Task TruncatedStream_Throws_EndOfStreamException()
    {
        await Should.ThrowAsync<EndOfStreamException>(
            () => FrameDecoder.ReadFrameAsync(new MemoryStream([0x81])));
    }

    [Fact]
    public async Task TextFrame_GetTextPayload_ReturnsUtf8String()
    {
        const string msg = "Hello, FrameWrench!";
        var encoded = System.Text.Encoding.UTF8.GetBytes(msg);
        var frame = await Decode(BuildServerFrame(FrameOpCode.Text, true, encoded));
        frame.GetTextPayload().ShouldBe(msg);
    }

    [Fact]
    public async Task SixtyFourBitLength_MsbSet_Throws_ProtocolException()
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x82);
        ms.WriteByte(0x7F);
        ms.Write([0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 0, 8);
        ms.Seek(0, SeekOrigin.Begin);

        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(ms));
        ex.Message.ShouldContain("most-significant bit");
    }

    [Fact]
    public async Task FragmentedCloseFrame_Throws_ProtocolException()
    {
        var wire = new byte[] { 0x08, 0x00 };
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(new MemoryStream(wire)));
        ex.Message.ShouldContain("fragmented");
    }

    [Fact]
    public async Task FragmentedPongFrame_Throws_ProtocolException()
    {
        var wire = new byte[] { 0x0A, 0x00 };
        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => FrameDecoder.ReadFrameAsync(new MemoryStream(wire)));
        ex.Message.ShouldContain("fragmented");
    }

    [Fact]
    public async Task CloseFrame_StatusCodeOnly_HasEmptyReason()
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)WebSocketCloseStatus.NormalClosure);

        var frame = await Decode(BuildServerFrame(FrameOpCode.Close, true, payload));
        frame.GetCloseData(out var status, out var reason);

        status.ShouldBe(WebSocketCloseStatus.NormalClosure);
        reason.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task CloseFrame_OneBytePayload_ReturnsNullStatus()
    {
        var frame = await Decode(BuildServerFrame(FrameOpCode.Close, true, [0x03]));
        frame.GetCloseData(out var status, out var reason);
        status.ShouldBeNull();
        reason.ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(65535)]
    [InlineData(65536)]
    public async Task EncoderDecoder_RoundTrip_PreservesPayload(int payloadLen)
    {
        var original = new byte[payloadLen];
        new Random(payloadLen).NextBytes(original);

        var frame = new WebSocketFrame(FrameOpCode.Binary, true, original);
        using var ms = new MemoryStream();
        await FrameEncoder.WriteAsync(ms, frame, masked: false);
        ms.Seek(0, SeekOrigin.Begin);

        var decoded = await FrameDecoder.ReadFrameAsync(ms);
        decoded.Payload.ToArray().ShouldBe(original);
        decoded.OpCode.ShouldBe(FrameOpCode.Binary);
        decoded.IsFinal.ShouldBeTrue();
    }
}
