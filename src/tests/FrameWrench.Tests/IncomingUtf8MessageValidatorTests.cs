using System.Text;
using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class IncomingUtf8MessageValidatorTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static WebSocketFrame T(byte[] p, bool fin = true) =>
        new(FrameOpCode.Text, fin, p);

    private static WebSocketFrame B(byte[] p, bool fin = true) =>
        new(FrameOpCode.Binary, fin, p);

    private static WebSocketFrame C(byte[] p, bool fin) =>
        new(FrameOpCode.Continuation, fin, p);

    [Fact]
    public void SingleTextFrame_ValidUtf8_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("hello")));
    }

    [Fact]
    public void SingleTextFrame_InvalidUtf8_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(T([0xFF, 0xFE])));
    }

    [Fact]
    public void SingleTextFrame_InvalidUtf8_WhenValidationDisabled_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T([0xFF, 0xFE]), validateUtf8: false);
    }

    [Fact]
    public void SingleFinalText_WithUtf8ValidationDisabled_AllowsSubsequentMessage()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T([0xFF, 0xFE]), validateUtf8: false);
        v.OnDataFrame(T(U("second")), validateUtf8: false);
    }

    [Fact]
    public void ContinuationWithoutStart_WhenUtf8ValidationDisabled_StillThrows()
    {
        var v = new IncomingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(C([1], fin: true), validateUtf8: false));
    }

    [Fact]
    public void FragmentedText_ValidUtf8AcrossFragments_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("hel"), fin: false));
        v.OnDataFrame(C(U("lo"), fin: true));
    }

    [Fact]
    public void FragmentedText_InvalidUtf8OnlyWhenComplete_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("hel"), fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(C([0xFF], fin: true)));
    }

    [Fact]
    public void ContinuationWithoutStart_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(C(U("x"), fin: true)));
    }

    [Fact]
    public void NewTextWhileFragmentedText_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("a"), fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(T(U("b"), fin: true)));
    }

    [Fact]
    public void BinaryWhileFragmentedText_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("a"), fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(B([1], fin: true)));
    }

    [Fact]
    public void TextWhileFragmentedBinary_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([1], fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(T(U("x"), fin: true)));
    }

    [Fact]
    public void FragmentedBinary_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([1, 2], fin: false));
        v.OnDataFrame(C([3], fin: true));
    }

    [Fact]
    public void Reset_ClearsFragmentState()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("a"), fin: false));
        v.Reset();
        v.OnDataFrame(T(U("ok"), fin: true));
    }

    [Fact]
    public void ControlOpcode_NotPassedFromPump_ThrowsIfInvoked()
    {
        var v = new IncomingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(new WebSocketFrame(FrameOpCode.Close, true, new byte[] { 0x03, 0xE8 })));
    }

    [Fact]
    public void SingleFinalBinaryFrame_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([0xDE, 0xAD, 0xBE, 0xEF]));
    }

    [Fact]
    public void BinaryWhileFragmentedBinary_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([1, 2], fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(B([3, 4], fin: true)));
    }

    [Fact]
    public void Reset_ClearsBinaryFragmentState()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([1], fin: false));
        v.Reset();
        v.OnDataFrame(B([2], fin: true));
    }

    [Fact]
    public void MultipleIntermediateContinuations_ForText_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("hel"), fin: false));
        v.OnDataFrame(C(U("lo"), fin: false));
        v.OnDataFrame(C(U(" wo"), fin: false));
        v.OnDataFrame(C(U("rld"), fin: true));
    }

    [Fact]
    public void MultipleIntermediateContinuations_ForBinary_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B([1], fin: false));
        v.OnDataFrame(C([2], fin: false));
        v.OnDataFrame(C([3], fin: false));
        v.OnDataFrame(C([4], fin: true));
    }

    [Fact]
    public void AfterCompleteMessage_CanReceiveNextMessage()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("first"), fin: true));
        v.OnDataFrame(T(U("second"), fin: true));
    }

    [Fact]
    public void AfterCompleteFragmentedText_CanReceiveNextMessage()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(T(U("hel"), fin: false));
        v.OnDataFrame(C(U("lo"), fin: true));
        v.OnDataFrame(T(U("next"), fin: true));
    }
}
