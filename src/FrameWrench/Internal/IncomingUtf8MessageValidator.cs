using FrameWrench.Core;

namespace FrameWrench.Internal;

internal sealed class IncomingUtf8MessageValidator
{
    private enum FragKind { None, Text, Binary }

    private FragKind _kind;
    private readonly Utf8IncrementalValidator _utf8 = new();

    public void Reset()
    {
        EndMessage();
    }

    private void EndMessage()
    {
        _kind = FragKind.None;
        _utf8.Reset();
    }

    public void OnDataFrame(WebSocketFrame frame, bool validateUtf8 = true)
    {
        switch (frame.OpCode)
        {
            case FrameOpCode.Text:
                if (_kind == FragKind.Text)
                    throw FrameWrenchErrors.Fragmentation(
                        "Received a new Text frame while a fragmented Text message was in progress.",
                        outbound: false,
                        actual: FrameOpCode.Text);

                if (_kind == FragKind.Binary)
                    throw FrameWrenchErrors.Fragmentation(
                        "Received a Text frame while a fragmented Binary message was in progress.",
                        outbound: false,
                        actual: FrameOpCode.Text);

                if (validateUtf8)
                    _utf8.ValidateFragment(frame.Payload.Span, frame.IsFinal, inbound: true);

                if (!frame.IsFinal)
                    _kind = FragKind.Text;
                else
                    EndMessage();

                break;

            case FrameOpCode.Binary:
                if (_kind != FragKind.None)
                    throw FrameWrenchErrors.Fragmentation(
                        "Received a Binary frame while a fragmented message was still in progress.",
                        outbound: false,
                        actual: FrameOpCode.Binary);

                if (!frame.IsFinal)
                    _kind = FragKind.Binary;

                break;

            case FrameOpCode.Continuation:
                if (_kind == FragKind.None)
                    throw FrameWrenchErrors.Fragmentation(
                        "Received a Continuation frame without a preceding Text or Binary frame.",
                        outbound: false,
                        actual: FrameOpCode.Continuation);

                if (_kind == FragKind.Text && validateUtf8)
                {
                    try
                    {
                        _utf8.ValidateFragment(frame.Payload.Span, frame.IsFinal, inbound: true);
                    }
                    finally
                    {
                        if (frame.IsFinal)
                            EndMessage();
                    }
                }
                else if (frame.IsFinal)
                {
                    EndMessage();
                }

                break;

            default:
                throw FrameWrenchErrors.Fragmentation(
                    $"Unexpected opcode in message validator: {frame.OpCode}.",
                    outbound: false,
                    actual: frame.OpCode);
        }
    }
}
