using FrameWrench;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class FrameWrenchOptionsTests
{
    [Fact]
    public void Build_Default_HasExpectedLimits()
    {
        var options = FrameWrenchOptions.Create().Build();

        options.MaxFramePayloadBytes.ShouldBe(64 * 1024 * 1024);
        options.FailOnInvalidIncomingUtf8.ShouldBeTrue();
        options.ValidateOutgoingMessages.ShouldBeTrue();
        options.SingleFrameConsumer.ShouldBeFalse();
    }

    [Fact]
    public void Build_WithExtraHeader_ExposesImmutableSnapshot()
    {
        var options = FrameWrenchOptions.Create()
            .WithExtraHeader("X-Test", "value")
            .Build();

        options.ExtraHeaders["X-Test"].ShouldBe("value");
        options.ExtraHeaders.ShouldNotBeSameAs(FrameWrenchOptions.Default.ExtraHeaders);
    }

    [Fact]
    public void Build_WithSubProtocol_SnapshotsList()
    {
        var options = FrameWrenchOptions.Create()
            .WithSubProtocol("chat")
            .Build();

        options.SubProtocols.ShouldBe(["chat"]);
    }

    [Theory]
    [InlineData("Authorization", "token\r\nX-Injected: yes")]
    [InlineData("X-Custom", "value\ninjected")]
    [InlineData("X-Custom", "has\0nul")]
    public void Build_HeaderInjection_Throws(string name, string value)
    {
        var ex = Should.Throw<ArgumentException>(() =>
            FrameWrenchOptions.Create()
                .WithExtraHeader(name, value)
                .Build());

        ex.Message.ShouldContain("FW-HANDSHAKE-HEADER-INVALID");
    }

    [Theory]
    [InlineData("Upgrade")]
    [InlineData("Sec-WebSocket-Key")]
    [InlineData("Host")]
    public void Build_ReservedHeaderOverride_Throws(string reserved)
    {
        var ex = Should.Throw<ArgumentException>(() =>
            FrameWrenchOptions.Create()
                .WithExtraHeader(reserved, "override")
                .Build());

        ex.Message.ShouldContain("FW-HANDSHAKE-HEADER-INVALID");
        ex.Message.ShouldContain("cannot be overridden");
    }

    [Fact]
    public void Build_EmptyHeaderName_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            FrameWrenchOptions.Create()
                .WithExtraHeader("", "value")
                .Build());
    }
}
