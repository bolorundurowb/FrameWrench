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
            v.OnDataFrame(T(new byte[] { 0xFF, 0xFE })));
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
            v.OnDataFrame(C(new byte[] { 0xFF }, fin: true)));
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
            v.OnDataFrame(B(new byte[] { 1 }, fin: true)));
    }

    [Fact]
    public void TextWhileFragmentedBinary_Throws()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B(new byte[] { 1 }, fin: false));
        Should.Throw<WebSocketProtocolException>(() =>
            v.OnDataFrame(T(U("x"), fin: true)));
    }

    [Fact]
    public void FragmentedBinary_DoesNotThrow()
    {
        var v = new IncomingUtf8MessageValidator();
        v.OnDataFrame(B(new byte[] { 1, 2 }, fin: false));
        v.OnDataFrame(C(new byte[] { 3 }, fin: true));
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
}
