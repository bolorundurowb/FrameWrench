using FrameWrench.Core;

namespace FrameWrench.Internal;

internal sealed class OutgoingUtf8MessageValidator
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

    public void OnDataFrame(WebSocketFrame frame)
    {
        switch (frame.OpCode)
        {
            case FrameOpCode.Text:
                if (_kind != FragKind.None)
                {
                    throw FrameWrenchErrors.Fragmentation(
                        $"Cannot send a new Text frame while a fragmented {(_kind == FragKind.Text ? "Text" : "Binary")} message is in progress.",
                        outbound: true,
                        actual: FrameOpCode.Text);
                }

                _utf8.ValidateFragment(frame.Payload.Span, frame.IsFinal, inbound: false);

                if (!frame.IsFinal)
                    _kind = FragKind.Text;
                else
                    EndMessage();

                break;

            case FrameOpCode.Binary:
                if (_kind != FragKind.None)
                {
                    throw FrameWrenchErrors.Fragmentation(
                        $"Cannot send a Binary frame while a fragmented {(_kind == FragKind.Text ? "Text" : "Binary")} message is in progress.",
                        outbound: true,
                        actual: FrameOpCode.Binary);
                }

                if (!frame.IsFinal)
                    _kind = FragKind.Binary;

                break;

            case FrameOpCode.Continuation:
                if (_kind == FragKind.None)
                {
                    throw FrameWrenchErrors.Fragmentation(
                        "Cannot send a Continuation frame without a preceding Text or Binary frame.",
                        outbound: true,
                        actual: FrameOpCode.Continuation);
                }

                if (_kind == FragKind.Text)
                {
                    try
                    {
                        _utf8.ValidateFragment(frame.Payload.Span, frame.IsFinal, inbound: false);
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
                    $"Unexpected opcode in outgoing message validator: {frame.OpCode}.",
                    outbound: true,
                    actual: frame.OpCode);
        }
    }
}
