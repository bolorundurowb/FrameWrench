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
}
