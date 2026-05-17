using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// A fully reassembled application data message produced by
/// <see cref="FrameWrench.FrameWrenchClient.ReceiveMessageAsync(System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// Control frames (Ping, Pong, Close) are not included. For unfragmented messages,
/// <see cref="Frames"/> contains a single element. For fragmented messages, fragments appear
/// in transmission order.
/// </remarks>
public sealed class WebSocketMessage
{
    /// <summary>
    /// Gets the message type: <see cref="FrameOpCode.Text"/> or <see cref="FrameOpCode.Binary"/>.
    /// </summary>
    public FrameOpCode MessageType { get; }

    /// <summary>
    /// Gets the concatenated payload bytes across all fragments.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Gets the individual frames that compose this message, in order.
    /// </summary>
    public IReadOnlyList<WebSocketFrame> Frames { get; }

    /// <summary>Initialises a new <see cref="WebSocketMessage"/>.</summary>
    internal WebSocketMessage(
        FrameOpCode messageType,
        ReadOnlyMemory<byte> payload,
        IReadOnlyList<WebSocketFrame> frames)
    {
        MessageType = messageType;
        Payload = payload;
        Frames = frames;
    }

    /// <summary>
    /// Decodes <see cref="Payload"/> as a UTF-8 string.
    /// </summary>
    /// <returns>The text content of the message.</returns>
    /// <remarks>
    /// Call only when <see cref="MessageType"/> is <see cref="FrameOpCode.Text"/>.
    /// When <see cref="FrameWrench.FrameWrenchOptions.FailOnInvalidIncomingUtf8"/> is
    /// <c>true</c> (default), invalid UTF-8 causes the connection to abort before this
    /// method runs.
    /// </remarks>
    public string GetText() =>
        Utf8StringUtil.GetString(Payload);

    /// <inheritdoc/>
    public override string ToString() =>
        $"WebSocketMessage [{MessageType} totalLen={Payload.Length} fragments={Frames.Count}]";
}
