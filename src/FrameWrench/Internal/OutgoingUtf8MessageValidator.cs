using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>
/// Outbound-side checks (stricter than RFC requires of a sender): §5.4 fragmentation ordering
/// and, for Text, reassembled UTF-8 validation before bytes are written (RFC 6455 §8.1).
/// Mirrors fragmentation tracking in <see cref="IncomingUtf8MessageValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The validator is intended to be called from inside the client's send lock, so it does not
/// take its own lock. Pass every outbound non-control frame to <see cref="OnDataFrame"/>; the
/// validator silently ignores Binary fragmentation and only buffers Text bytes.
/// </para>
/// <para>
/// If validation fails midway through a multi-frame Text send, the caller has already
/// committed to that message over the wire. In that case throwing here gives the caller a
/// clear signal that the connection should be aborted; the message they were producing is
/// not RFC-compliant.
/// </para>
/// </remarks>
internal sealed class OutgoingUtf8MessageValidator
{
    private enum FragKind
    {
        None,
        Text,
        Binary,
    }

    private FragKind _kind;
    private List<byte>? _textBuf;

    /// <summary>Resets state, e.g. when the connection is re-initialised or torn down.</summary>
    public void Reset()
    {
        _kind = FragKind.None;
        _textBuf = null;
    }

    /// <summary>
    /// Validates ordering and (for completed Text messages) UTF-8 of an outbound frame.
    /// Control frames must not be passed here.
    /// </summary>
    public void OnDataFrame(WebSocketFrame frame)
    {
        switch (frame.OpCode)
        {
            case FrameOpCode.Text:
                if (_kind != FragKind.None)
                {
                    throw new WebSocketProtocolException(
                        "Cannot send a new Text frame while a fragmented " +
                        $"{(_kind == FragKind.Text ? "Text" : "Binary")} message is in progress. " +
                        "RFC 6455 §5.4 does not permit interleaved data messages.");
                }

                if (frame.IsFinal)
                {
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
                if (_kind != FragKind.None)
                {
                    throw new WebSocketProtocolException(
                        "Cannot send a Binary frame while a fragmented " +
                        $"{(_kind == FragKind.Text ? "Text" : "Binary")} message is in progress.");
                }

                if (!frame.IsFinal)
                    _kind = FragKind.Binary;

                break;

            case FrameOpCode.Continuation:
                if (_kind == FragKind.None)
                {
                    throw new WebSocketProtocolException(
                        "Cannot send a Continuation frame without a preceding Text or Binary frame.");
                }

                if (_kind == FragKind.Text)
                {
                    if (_textBuf is null)
                    {
                        throw new WebSocketProtocolException(
                            "Internal error: fragmented outbound Text state without an accumulator buffer.");
                    }

                    AppendPayload(_textBuf, frame.Payload);
                    if (frame.IsFinal)
                    {
                        // Reset state regardless of whether validation throws — once the message
                        // ends (good or bad) the validator should not keep buffering future bytes
                        // against a now-finished fragmentation sequence.
                        var pending = _textBuf.ToArray();
                        _textBuf = null;
                        _kind = FragKind.None;
                        Utf8Validator.ThrowIfInvalidUtf8(pending);
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
                    $"Unexpected opcode in outgoing UTF-8 validator: {frame.OpCode}.");
        }
    }

    private static void AppendPayload(List<byte> list, ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
            return;

        list.AddRange(payload.ToArray());
    }
}
