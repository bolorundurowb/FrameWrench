using System.Text;
using FrameWrench.Core;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class WebSocketMessageTests
{
    private static WebSocketMessage MakeText(string text)
    {
        ReadOnlyMemory<byte> payload = Encoding.UTF8.GetBytes(text);
        var frame = new WebSocketFrame(FrameOpCode.Text, isFinal: true, payload);
        return new WebSocketMessage(FrameOpCode.Text, payload, [frame]);
    }

    private static WebSocketMessage MakeBinary(byte[] data)
    {
        ReadOnlyMemory<byte> payload = data;
        var frame = new WebSocketFrame(FrameOpCode.Binary, isFinal: true, payload);
        return new WebSocketMessage(FrameOpCode.Binary, payload, [frame]);
    }

    // ── MessageType ───────────────────────────────────────────────────────────

    [Fact]
    public void MessageType_Text_IsPreserved()
    {
        MakeText("hello").MessageType.ShouldBe(FrameOpCode.Text);
    }

    [Fact]
    public void MessageType_Binary_IsPreserved()
    {
        MakeBinary([1, 2, 3]).MessageType.ShouldBe(FrameOpCode.Binary);
    }

    // ── Payload ───────────────────────────────────────────────────────────────

    [Fact]
    public void Payload_MatchesSuppliedData()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        MakeBinary(data).Payload.ToArray().ShouldBe(data);
    }

    [Fact]
    public void Payload_EmptyBytes_HasLengthZero()
    {
        MakeBinary([]).Payload.Length.ShouldBe(0);
    }

    // ── GetText ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetText_DecodesAscii()
    {
        MakeText("Hello, World!").GetText().ShouldBe("Hello, World!");
    }

    [Fact]
    public void GetText_DecodesMultiByteUtf8()
    {
        const string text = "こんにちは";
        MakeText(text).GetText().ShouldBe(text);
    }

    [Fact]
    public void GetText_EmptyPayload_ReturnsEmptyString()
    {
        MakeText(string.Empty).GetText().ShouldBe(string.Empty);
    }

    // ── Frames ────────────────────────────────────────────────────────────────

    [Fact]
    public void Frames_SingleFrame_HasCountOne()
    {
        MakeText("hi").Frames.Count.ShouldBe(1);
    }

    [Fact]
    public void Frames_Fragmented_PreservesAllFragments()
    {
        ReadOnlyMemory<byte> p1 = Encoding.UTF8.GetBytes("hel");
        ReadOnlyMemory<byte> p2 = Encoding.UTF8.GetBytes("lo");
        var f1 = new WebSocketFrame(FrameOpCode.Text, isFinal: false, p1);
        var f2 = new WebSocketFrame(FrameOpCode.Continuation, isFinal: true, p2);

        ReadOnlyMemory<byte> combined = Encoding.UTF8.GetBytes("hello");
        var msg = new WebSocketMessage(FrameOpCode.Text, combined, [f1, f2]);

        msg.Frames.Count.ShouldBe(2);
        msg.Frames[0].ShouldBeSameAs(f1);
        msg.Frames[1].ShouldBeSameAs(f2);
    }

    [Fact]
    public void Frames_FragmentedPayload_ReassemblesCorrectly()
    {
        ReadOnlyMemory<byte> p1 = Encoding.UTF8.GetBytes("Hello, ");
        ReadOnlyMemory<byte> p2 = Encoding.UTF8.GetBytes("World!");
        var f1 = new WebSocketFrame(FrameOpCode.Text, isFinal: false, p1);
        var f2 = new WebSocketFrame(FrameOpCode.Continuation, isFinal: true, p2);

        ReadOnlyMemory<byte> combined = Encoding.UTF8.GetBytes("Hello, World!");
        var msg = new WebSocketMessage(FrameOpCode.Text, combined, [f1, f2]);

        msg.GetText().ShouldBe("Hello, World!");
        msg.Payload.Length.ShouldBe(Encoding.UTF8.GetByteCount("Hello, World!"));
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ContainsMessageType()
    {
        MakeText("hi").ToString().ShouldContain("Text");
    }

    [Fact]
    public void ToString_ContainsTotalPayloadLength()
    {
        var text = "hi";
        var msg = MakeText(text);
        msg.ToString().ShouldContain($"totalLen={Encoding.UTF8.GetByteCount(text)}");
    }

    [Fact]
    public void ToString_ContainsFragmentCount()
    {
        MakeText("hi").ToString().ShouldContain("fragments=1");
    }

    [Fact]
    public void ToString_Binary_ContainsMessageType()
    {
        MakeBinary([1]).ToString().ShouldContain("Binary");
    }
}
