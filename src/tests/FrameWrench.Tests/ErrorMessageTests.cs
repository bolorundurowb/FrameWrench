using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class ErrorMessageTests
{
    [Fact]
    public void MaskedServerFrame_ContainsErrorCodeAndHelp()
    {
        var ex = FrameWrenchErrors.MaskedServerFrame(WebSocketState.Open);
        ex.ErrorCode.ShouldBe("FW-PROTO-MASKED-SERVER-FRAME");
        ex.Message.ShouldContain("error[FW-PROTO-MASKED-SERVER-FRAME]");
        ex.Message.ShouldContain("help:");
        ex.Message.ShouldContain("RFC 6455 §5.1");
    }

    [Fact]
    public void InvalidState_ContainsAllowedStates()
    {
        var ex = FrameWrenchErrors.InvalidState(
            WebSocketState.None,
            "SendTextAsync",
            WebSocketState.Open);

        ex.ErrorCode.ShouldBe("FW-STATE-INVALID");
        ex.Message.ShouldContain("Open");
        ex.Detail.Operation.ShouldBe("SendTextAsync");
    }

    [Fact]
    public void HandshakeAcceptMismatch_ContainsStatusLine()
    {
        var ex = FrameWrenchErrors.HandshakeAcceptMismatch(
            "expected",
            "actual",
            "HTTP/1.1 101 Switching Protocols");

        ex.StatusLine!.ShouldContain("101");
        ex.ErrorCode.ShouldBe("FW-HANDSHAKE-ACCEPT");
    }
}
