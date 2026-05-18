using FrameWrench.Core;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class CloseFrameValidatorTests
{
    [Theory]
    [InlineData(1000, true)]
    [InlineData(1005, false)]
    [InlineData(1006, false)]
    [InlineData(1015, false)]
    [InlineData(4000, true)]
    public void IsValidWireStatus_ClassifiesCodes(ushort code, bool expected) =>
        CloseFrameValidator.IsValidWireStatus(code).ShouldBe(expected);

    [Fact]
    public void ValidateForSend_PseudoCode_Throws()
    {
        Should.Throw<WebSocketProtocolException>(() =>
            CloseFrameValidator.ValidateForSend((WireCloseStatus)1005, null));
    }

    [Fact]
    public void ThrowIfInvalidOnWire_InvalidInboundStatus_Throws()
    {
        var payload = new byte[] { 0x03, 0xEE };
        Should.Throw<WebSocketProtocolException>(() =>
            CloseFrameValidator.ThrowIfInvalidOnWire(payload, validateUtf8: false));
    }

    [Fact]
    public void Parse_ApplicationDefinedCode_PreservesStatusCodeWithoutEnum()
    {
        var payload = new byte[] { 0x0F, 0xA0 };
        var info = CloseFrameValidator.Parse(payload);

        info.StatusCode.ShouldBe((ushort)4000);
        info.Status.ShouldBeNull();
    }

    [Fact]
    public void ValidateForSend_ApplicationDefinedCode_DoesNotThrow()
    {
        Should.NotThrow(() => CloseFrameValidator.ValidateForSend((ushort)4000, null));
    }

    [Fact]
    public void ValidateForSend_ReasonTooLong_Throws()
    {
        var longReason = new string('x', 200);
        var ex = Should.Throw<WebSocketProtocolException>(() =>
            CloseFrameValidator.ValidateForSend(WireCloseStatus.NormalClosure, longReason));

        ex.ErrorCode.ShouldBe("FW-PROTO-CLOSE-REASON-LONG");
    }

    [Fact]
    public void ThrowIfInvalidOnWire_ValidApplicationCode_DoesNotThrow()
    {
        var payload = new byte[] { 0x0F, 0xA0 };
        Should.NotThrow(() =>
            CloseFrameValidator.ThrowIfInvalidOnWire(payload, validateUtf8: false));
    }

    [Theory]
    [InlineData(1000, WireCloseStatus.NormalClosure)]
    [InlineData(4000, null)]
    public void CreateEchoFrame_PreservesWireStatus(ushort code, WireCloseStatus? expectedStatus)
    {
        var info = CloseFrameValidator.Parse(new byte[] { (byte)(code >> 8), (byte)(code & 0xFF) });
        var echo = CloseFrameValidator.CreateEchoFrame(info);

        echo.GetCloseInfo().StatusCode.ShouldBe(code);
        echo.GetCloseInfo().Status.ShouldBe(expectedStatus);
    }

    [Fact]
    public void CreateEchoFrame_EmptyPayload_UsesNormalClosure()
    {
        var info = CloseFrameValidator.Parse(ReadOnlyMemory<byte>.Empty);
        var echo = CloseFrameValidator.CreateEchoFrame(info);

        echo.GetCloseInfo().Status.ShouldBe(WireCloseStatus.NormalClosure);
    }
}
