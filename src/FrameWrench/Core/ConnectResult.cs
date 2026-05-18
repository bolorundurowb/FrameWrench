namespace FrameWrench.Core;

/// <summary>
/// Result of a successful <see cref="FrameWrench.FrameWrenchClient.ConnectAsync"/> call.
/// </summary>
public readonly struct ConnectResult
{
    /// <summary>Initialises a connect result.</summary>
    public ConnectResult(string? selectedSubProtocol) =>
        SelectedSubProtocol = selectedSubProtocol;

    /// <summary>
    /// Subprotocol selected by the server, or <c>null</c> when none was negotiated.
    /// </summary>
    public string? SelectedSubProtocol { get; }
}
