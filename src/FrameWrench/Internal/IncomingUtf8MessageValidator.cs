using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>
/// Tracks fragmented Text/Binary data messages from the server and validates that complete
/// Text messages are well-formed UTF-8 per RFC 6455 §8.1.
/// </summary>
internal sealed class IncomingUtf8MessageValidator
{
    private enum FragKind
    {
        None,
        Text,
        Binary,
    }

    private FragKind _kind;
    private List<byte>? _textBuf;

    public void Reset()
    {
        _kind = FragKind.None;
        _textBuf = null;
    }

    /// <summary>
    /// Validates §5.4 fragmentation ordering. When <paramref name="validateUtf8"/> is
    /// <c>true</c>, also validates completed Text messages per RFC 6455 §8.1.
    /// Control frames must not be passed here.
    /// </summary>
    public void OnDataFrame(WebSocketFrame frame, bool validateUtf8 = true)
    {
        switch (frame.OpCode)
        {
            case FrameOpCode.Text:
                if (_kind == FragKind.Text)
                {
                    throw new WebSocketProtocolException(
                        "Received a new Text frame while a fragmented Text message was in progress. " +
                        "RFC 6455 §5.4 does not permit interleaved data messages.");
                }

                if (_kind == FragKind.Binary)
                {
                    throw new WebSocketProtocolException(
                        "Received a Text frame while a fragmented Binary message was in progress.");
                }

                if (frame.IsFinal)
                {
                    if (validateUtf8)
                        Utf8Validator.ThrowIfInvalidUtf8(frame.Payload.Span);
                }
                else
                {
                    _textBuf = new List<byte>(frame.Payload.Length);
                    AppendPayload(_textBuf, frame.Payload);
                    _kind = FragKind.Text;
                }

                break;

            case FrameOpCode.Binary:
                if (_kind == FragKind.Text || _kind == FragKind.Binary)
                {
                    throw new WebSocketProtocolException(
                        "Received a Binary frame while a fragmented message was still in progress.");
                }

                if (!frame.IsFinal)
                    _kind = FragKind.Binary;

                break;

            case FrameOpCode.Continuation:
                if (_kind == FragKind.None)
                {
                    throw new WebSocketProtocolException(
                        "Received a Continuation frame without a preceding Text or Binary frame.");
                }

                if (_kind == FragKind.Text)
                {
                    if (_textBuf is null)
                    {
                        throw new WebSocketProtocolException(
                            "Internal error: fragmented Text state without an accumulator buffer.");
                    }

                    AppendPayload(_textBuf, frame.Payload);
                    if (frame.IsFinal)
                    {
                        if (validateUtf8)
                            Utf8Validator.ThrowIfInvalidUtf8(_textBuf.ToArray());
                        _textBuf = null;
                        _kind = FragKind.None;
                    }
                }
                else
                {
                    if (frame.IsFinal)
                        _kind = FragKind.None;
                }

                break;

            default:
                throw new WebSocketProtocolException(
                    $"Unexpected opcode in UTF-8 message validator: {frame.OpCode}.");
        }
    }

    private static void AppendPayload(List<byte> list, ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
            return;

        // netstandard2.0 has no List<byte>.AddRange(ReadOnlySpan<byte>); one array + bulk add
        // beats per-byte Add for large fragmented text payloads.
        list.AddRange(payload.ToArray());
    }
}
