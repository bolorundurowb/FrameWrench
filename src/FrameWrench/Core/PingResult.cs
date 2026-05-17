namespace FrameWrench.Core;

/// <summary>
/// Result of <see cref="FrameWrench.FrameWrenchClient.PingAsync"/>.
/// </summary>
public readonly struct PingResult
{
    /// <summary>Initialises a ping result.</summary>
    public PingResult(bool pongReceived, TimeSpan elapsed)
    {
        PongReceived = pongReceived;
        Elapsed = elapsed;
    }

    /// <summary><c>true</c> when a matching Pong arrived before the timeout.</summary>
    public bool PongReceived { get; }

    /// <summary>Elapsed time from sending the Ping to completion (success or timeout).</summary>
    public TimeSpan Elapsed { get; }
}
