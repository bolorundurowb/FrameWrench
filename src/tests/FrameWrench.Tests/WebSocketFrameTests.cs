using FrameWrench.Core;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class WebSocketFrameTests
{
    [Fact]
    public void Text_Factory_SetsCorrectFields()
    {
        var frame = WebSocketFrame.Text("Hello");
        frame.OpCode.ShouldBe(FrameOpCode.Text);
        frame.IsFinal.ShouldBeTrue();
        frame.GetTextPayload().ShouldBe("Hello");
    }

    [Fact]
    public void Text_Fragment_HasFINFalse()
    {
        WebSocketFrame.Text("frag", isFinal: false).IsFinal.ShouldBeFalse();
    }

    [Fact]
    public void Binary_Factory_SetsCorrectFields()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = WebSocketFrame.Binary(data);
        frame.OpCode.ShouldBe(FrameOpCode.Binary);
        frame.IsFinal.ShouldBeTrue();
        frame.Payload.ToArray().ShouldBe(data);
    }

    [Fact]
    public void Continuation_Factory_SetsCorrectFields()
    {
        var frame = WebSocketFrame.Continuation(new byte[] { 0xAB }, isFinal: true);
        frame.OpCode.ShouldBe(FrameOpCode.Continuation);
        frame.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void Ping_Factory_SetsCorrectFields()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var frame = WebSocketFrame.Ping(payload);
        frame.OpCode.ShouldBe(FrameOpCode.Ping);
        frame.IsFinal.ShouldBeTrue();
        frame.IsControl.ShouldBeTrue();
        frame.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void Pong_Factory_SetsCorrectFields()
    {
        var frame = WebSocketFrame.Pong(new byte[] { 0xFF });
        frame.OpCode.ShouldBe(FrameOpCode.Pong);
        frame.IsControl.ShouldBeTrue();
    }

    [Fact]
    public void Close_Factory_EncodesStatusCode_NormalClosure()
    {
        var frame = WebSocketFrame.Close(WebSocketCloseStatus.NormalClosure);
        frame.OpCode.ShouldBe(FrameOpCode.Close);
        frame.Payload.Length.ShouldBe(2);
        frame.Payload.Span[0].ShouldBe((byte)0x03);
        frame.Payload.Span[1].ShouldBe((byte)0xE8);
    }

    [Fact]
    public void Close_Factory_EncodesStatusAndReason()
    {
        var frame = WebSocketFrame.Close(WebSocketCloseStatus.GoingAway, "bye");
        frame.GetCloseData(out var status, out var reason);
        status.ShouldBe(WebSocketCloseStatus.GoingAway);
        reason.ShouldBe("bye");
    }

    [Fact]
    public void GetCloseData_OnNonCloseFrame_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WebSocketFrame.Text("x").GetCloseData(out _, out _));
    }

    [Fact]
    public void IsControl_TrueForCloseAndPingAndPong()
    {
        WebSocketFrame.Close().IsControl.ShouldBeTrue();
        WebSocketFrame.Ping().IsControl.ShouldBeTrue();
        WebSocketFrame.Pong().IsControl.ShouldBeTrue();
    }

    [Fact]
    public void IsControl_FalseForDataFrames()
    {
        WebSocketFrame.Text("x").IsControl.ShouldBeFalse();
        WebSocketFrame.Binary(Array.Empty<byte>()).IsControl.ShouldBeFalse();
        WebSocketFrame.Continuation(Array.Empty<byte>(), true).IsControl.ShouldBeFalse();
    }

    [Fact]
    public void GetTextPayload_DecodesUtf8()
    {
        const string msg = "こんにちは";
        WebSocketFrame.Text(msg).GetTextPayload().ShouldBe(msg);
    }

    [Fact]
    public void Text_EmptyString_HasEmptyPayload()
    {
        var frame = WebSocketFrame.Text(string.Empty);
        frame.OpCode.ShouldBe(FrameOpCode.Text);
        frame.Payload.Length.ShouldBe(0);
        frame.GetTextPayload().ShouldBe(string.Empty);
    }

    [Fact]
    public void Binary_Fragment_HasFINFalse()
    {
        var frame = WebSocketFrame.Binary(new byte[] { 1, 2 }, isFinal: false);
        frame.IsFinal.ShouldBeFalse();
        frame.OpCode.ShouldBe(FrameOpCode.Binary);
    }

    [Fact]
    public void Continuation_Fragment_HasFINFalse()
    {
        var frame = WebSocketFrame.Continuation(new byte[] { 0x01 }, isFinal: false);
        frame.IsFinal.ShouldBeFalse();
        frame.OpCode.ShouldBe(FrameOpCode.Continuation);
    }

    [Fact]
    public void Ping_NoPayload_HasEmptyPayload()
    {
        var frame = WebSocketFrame.Ping();
        frame.Payload.IsEmpty.ShouldBeTrue();
        frame.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void Pong_NoPayload_HasEmptyPayload()
    {
        var frame = WebSocketFrame.Pong();
        frame.Payload.IsEmpty.ShouldBeTrue();
        frame.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void Close_NullReason_GetCloseData_ReturnsEmptyReason()
    {
        var frame = WebSocketFrame.Close(WebSocketCloseStatus.NormalClosure, null);
        frame.GetCloseData(out var status, out var reason);
        status.ShouldBe(WebSocketCloseStatus.NormalClosure);
        reason.ShouldBe(string.Empty);
    }

    [Fact]
    public void GetCloseData_OneBytePayload_ReturnsNullStatus()
    {
        var frame = new WebSocketFrame(FrameOpCode.Close, true, new byte[] { 0x03 });
        frame.GetCloseData(out var status, out var reason);
        status.ShouldBeNull();
        reason.ShouldBe(string.Empty);
    }

    [Fact]
    public void ToString_ContainsOpCodeAndLength()
    {
        var frame = WebSocketFrame.Text("hi");
        var str = frame.ToString();
        str.ShouldContain("Text");
        str.ShouldContain("len=2");
    }

    [Fact]
    public void ToString_Fragment_ContainsFragmentLabel()
    {
        var frame = WebSocketFrame.Text("part", isFinal: false);
        frame.ToString().ShouldContain("fragment");
    }
}
