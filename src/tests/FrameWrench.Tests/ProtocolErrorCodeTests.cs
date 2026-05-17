using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

/// <summary>
/// Locks stable <see cref="FrameWrenchException.ErrorCode"/> values and message shape for major failures.
/// </summary>
public class ProtocolErrorCodeTests
{
    public static TheoryData<Func<FrameWrenchException>> ProtocolErrors => new()
    {
        { () => FrameWrenchErrors.MaskedServerFrame(WebSocketState.Open) },
        { () => FrameWrenchErrors.NonZeroRsv(true, false, false) },
        { () => FrameWrenchErrors.ReservedOpcode(0x3, fin: true) },
        { () => FrameWrenchErrors.FragmentedControlFrame(FrameOpCode.Close) },
        { () => FrameWrenchErrors.ControlPayloadTooLarge(200) },
        { () => FrameWrenchErrors.InvalidPayloadLengthMsb() },
        { () => FrameWrenchErrors.PayloadLengthOverflow() },
        { () => FrameWrenchErrors.PayloadTooLarge(200, 100, nameof(FrameWrenchOptions.MaxFramePayloadBytes)) },
        { () => FrameWrenchErrors.InvalidCloseStatus(1005, inbound: true) },
        { () => FrameWrenchErrors.CloseReasonTooLong(200) },
        { () => FrameWrenchErrors.InvalidUtf8(inbound: true, payloadKind: "Text") },
        { () => FrameWrenchErrors.Fragmentation("test", outbound: false, actual: FrameOpCode.Continuation) },
    };

    [Theory]
    [MemberData(nameof(ProtocolErrors))]
    public void ProtocolException_ContainsErrorCodeAndHelp(Func<FrameWrenchException> factory)
    {
        var ex = factory();
        ex.ShouldBeOfType<WebSocketProtocolException>();
        ex.Message.ShouldStartWith($"error[{ex.ErrorCode}]:");
        ex.Message.ShouldContain("help:");
        ex.Detail.Suggestions.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void PeerClosed_ContainsCloseInfoContext()
    {
        var close = new CloseFrameInfo(4000, null, "bye");
        var ex = FrameWrenchErrors.PeerClosed(close, nameof(FrameWrench.FrameWrenchClient.ReceiveMessageAsync));

        ex.ShouldBeOfType<WebSocketClosedByPeerException>();
        ex.ErrorCode.ShouldBe("FW-PEER-CLOSED");
        ex.CloseInfo.StatusCode.ShouldBe((ushort)4000);
        ex.Message.ShouldContain("4000");
    }

    [Fact]
    public void HandshakeNon101_ContainsStatusLine()
    {
        var ex = FrameWrenchErrors.HandshakeNon101("HTTP/1.1 403 Forbidden");
        ex.ErrorCode.ShouldBe("FW-HANDSHAKE-NON-101");
        ex.StatusLine!.ShouldContain("403");
    }

    [Fact]
    public void ConnectionClosedNoFrames_ContainsState()
    {
        var ex = FrameWrenchErrors.ConnectionClosedNoFrames(WebSocketState.Closed);
        ex.ErrorCode.ShouldBe("FW-CONN-CLOSED");
        ex.Detail.Context["connectionState"].ShouldBe("Closed");
    }
}
