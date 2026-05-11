namespace FrameWrench.Core;

/// <summary>
/// A fully-reassembled WebSocket message, produced by
/// <see cref="FrameWrenchClient.ReceiveMessageAsync"/>.
/// </summary>
/// <remarks>
/// For messages that arrive as a single un-fragmented frame, <see cref="Frames"/>
/// contains exactly one element.  For fragmented messages all fragments are stored
/// in order so callers can inspect individual frames if needed.
/// </remarks>
public sealed class WebSocketMessage
{
    /// <summary>
    /// The message type: <see cref="FrameOpCode.Text"/> or <see cref="FrameOpCode.Binary"/>.
    /// </summary>
    public FrameOpCode MessageType { get; }

    /// <summary>The reassembled payload across all fragments.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The individual frames that compose this message, in order.</summary>
    public IReadOnlyList<WebSocketFrame> Frames { get; }

    /// <summary>Initialises a new <see cref="WebSocketMessage"/>.</summary>
    internal WebSocketMessage(
        FrameOpCode                 messageType,
        ReadOnlyMemory<byte>        payload,
        IReadOnlyList<WebSocketFrame> frames)
    {
        MessageType = messageType;
        Payload     = payload;
        Frames      = frames;
    }

    /// <summary>
    /// Decodes the payload as UTF-8 text.  Should only be called for
    /// <see cref="FrameOpCode.Text"/> messages.
    /// </summary>
    public string GetText() =>
        System.Text.Encoding.UTF8.GetString(Payload.ToArray());

    /// <inheritdoc/>
    public override string ToString() =>
        $"WebSocketMessage [{MessageType} totalLen={Payload.Length} fragments={Frames.Count}]";
}
