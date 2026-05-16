using System.Text;
using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class OutgoingUtf8MessageValidatorTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static WebSocketFrame T(byte[] p, bool fin = true) =>
        new(FrameOpCode.Text, fin, p);

    private static WebSocketFrame B(byte[] p, bool fin = true) =>
        new(FrameOpCode.Binary, fin, p);

    private static WebSocketFrame C(byte[] p, bool fin) =>
        new(FrameOpCode.Continuation, fin, p);

    [Fact]
    public void SingleFinalText_ValidUtf8_DoesNotThrow()
    {
        var v = new OutgoingUtf8MessageValidator();
        v.OnDataFrame(T(U("café")));
    }

    [Fact]
    public void SingleFinalText_InvalidUtf8_Throws()
    {
        var v = new OutgoingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(() => v.OnDataFrame(T(new byte[] { 0xFF })));
    }

    [Fact]
    public void FragmentedText_ValidAcrossFragments_DoesNotThrow()
    {
        var v = new OutgoingUtf8MessageValidator();
        // "café" split mid-codepoint: 'caf' (3 bytes ASCII) + start of é (0xC3) + final 0xA9
        var bytes = U("café");
        v.OnDataFrame(T(bytes.AsSpan(0, 3).ToArray(), fin: false));
        v.OnDataFrame(C(bytes.AsSpan(3, 1).ToArray(), fin: false));
        v.OnDataFrame(C(bytes.AsSpan(4).ToArray(), fin: true));
    }

    [Fact]
    public void FragmentedText_InvalidUtf8OnFinalFrame_Throws()
    {
        var v = new OutgoingUtf8MessageValidator();
        v.OnDataFrame(T(U("café"), fin: false));
        Should.Throw<WebSocketProtocolException>(
            () => v.OnDataFrame(C(new byte[] { 0xFF }, fin: true)));
    }

    [Fact]
    public void FragmentedText_InvalidBytesSpanningFragmentBoundary_Throws()
    {
        var v = new OutgoingUtf8MessageValidator();
        // 0xC3 starts a 2-byte sequence but 0x28 is not a valid continuation byte.
        v.OnDataFrame(T(new byte[] { 0xC3 }, fin: false));
        Should.Throw<WebSocketProtocolException>(
            () => v.OnDataFrame(C(new byte[] { 0x28 }, fin: true)));
    }

    [Fact]
    public void TextWhileBinaryInProgress_Throws()
    {
        var v = new OutgoingUtf8MessageValidator();
        v.OnDataFrame(B(new byte[] { 1 }, fin: false));
        Should.Throw<WebSocketProtocolException>(
            () => v.OnDataFrame(T(U("nope"))));
    }

    [Fact]
    public void ContinuationWithoutPrecedingFrame_Throws()
    {
        var v = new OutgoingUtf8MessageValidator();
        Should.Throw<WebSocketProtocolException>(
            () => v.OnDataFrame(C(new byte[] { 1 }, fin: true)));
    }

    [Fact]
    public void StateResetsAfterFailedFinalFragment()
    {
        var v = new OutgoingUtf8MessageValidator();
        v.OnDataFrame(T(U("hi"), fin: false));
        Should.Throw<WebSocketProtocolException>(
            () => v.OnDataFrame(C(new byte[] { 0xFF }, fin: true)));

        // After a failure on the final fragment, validator state is clean enough to start fresh.
        v.OnDataFrame(T(U("after")));
    }

    [Fact]
    public void BinaryFragmented_DoesNotBuffer_AndCompletesCleanly()
    {
        var v = new OutgoingUtf8MessageValidator();
        v.OnDataFrame(B(new byte[] { 1, 2 }, fin: false));
        v.OnDataFrame(C(new byte[] { 3, 4 }, fin: true));
        // A subsequent fresh Text message should succeed.
        v.OnDataFrame(T(U("ok")));
    }
}
