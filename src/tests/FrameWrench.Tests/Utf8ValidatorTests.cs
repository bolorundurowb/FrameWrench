using System.Text;
using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class Utf8ValidatorTests
{
    [Fact]
    public void ThrowIfInvalidUtf8_Empty_DoesNotThrow()
    {
        Utf8Validator.ThrowIfInvalidUtf8(ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public void ThrowIfInvalidUtf8_Ascii_IsValid()
    {
        Utf8Validator.ThrowIfInvalidUtf8(Encoding.UTF8.GetBytes("FrameWrench"));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_TwoByteScalar_IsValid()
    {
        Utf8Validator.ThrowIfInvalidUtf8(Encoding.UTF8.GetBytes("\u00A3"));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_FourByteScalar_IsValid()
    {
        Utf8Validator.ThrowIfInvalidUtf8(Encoding.UTF8.GetBytes("\U0001F600"));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_LoneContinuationByte_Throws()
    {
        Should.Throw<WebSocketProtocolException>(() =>
            Utf8Validator.ThrowIfInvalidUtf8([0x80]));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_Lone0xFF_Throws()
    {
        Should.Throw<WebSocketProtocolException>(() =>
            Utf8Validator.ThrowIfInvalidUtf8([0xFF]));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_TruncatedSequence_Throws()
    {
        Should.Throw<WebSocketProtocolException>(() =>
            Utf8Validator.ThrowIfInvalidUtf8([0xE2, 0x82]));
    }

    [Fact]
    public void ThrowIfInvalidUtf8_OverlongEncoding_Throws()
    {
        Should.Throw<WebSocketProtocolException>(() =>
            Utf8Validator.ThrowIfInvalidUtf8([0xF0, 0x80, 0x80, 0xAF]));
    }

    [Fact]
    public void ThrowIfInvalidCloseReason_EmptyReason_DoesNotThrow()
    {
        var payload = new byte[] { 0x03, 0xE8 };
        Utf8Validator.ThrowIfInvalidCloseReason(payload);
    }

    [Fact]
    public void ThrowIfInvalidCloseReason_ValidReason_DoesNotThrow()
    {
        var reason = Encoding.UTF8.GetBytes("going away");
        var payload = new byte[2 + reason.Length];
        payload[0] = 0x03;
        payload[1] = 0xE8;
        reason.CopyTo(payload, 2);
        Utf8Validator.ThrowIfInvalidCloseReason(payload);
    }

    [Fact]
    public void ThrowIfInvalidCloseReason_InvalidUtf8InReason_Throws()
    {
        var payload = new byte[] { 0x03, 0xE8, 0xFF, 0xFF };
        Should.Throw<WebSocketProtocolException>(() =>
            Utf8Validator.ThrowIfInvalidCloseReason(payload));
    }

    [Fact]
    public void ThrowIfInvalidCloseReason_ZeroBytePayload_DoesNotThrow()
    {
        Utf8Validator.ThrowIfInvalidCloseReason(ReadOnlyMemory<byte>.Empty);
    }

    [Fact]
    public void ThrowIfInvalidCloseReason_OneBytePayload_DoesNotThrow()
    {
        Utf8Validator.ThrowIfInvalidCloseReason(new byte[] { 0x03 });
    }

    [Fact]
    public void ThrowIfInvalidUtf8_ThreeByteScalar_IsValid()
    {
        Utf8Validator.ThrowIfInvalidUtf8([0xE2, 0x82, 0xAC]);
    }

    [Fact]
    public void ThrowIfInvalidUtf8_EmbeddedNullByte_IsValid()
    {
        Utf8Validator.ThrowIfInvalidUtf8([(byte)'A', 0x00, (byte)'B']);
    }
}
