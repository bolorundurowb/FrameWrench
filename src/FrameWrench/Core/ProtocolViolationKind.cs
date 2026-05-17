namespace FrameWrench.Core;

/// <summary>
/// Categories of RFC 6455 protocol violations for programmatic handling.
/// </summary>
public enum ProtocolViolationKind
{
    /// <summary>Unclassified protocol error.</summary>
    Other = 0,

    /// <summary>Server sent a masked frame.</summary>
    MaskedServerFrame = 1,

    /// <summary>Non-zero RSV bits without negotiated extension.</summary>
    NonZeroRsv = 2,

    /// <summary>Reserved or unknown opcode.</summary>
    ReservedOpcode = 3,

    /// <summary>Control frame with FIN cleared.</summary>
    FragmentedControlFrame = 4,

    /// <summary>Control frame payload exceeds 125 bytes.</summary>
    ControlPayloadTooLarge = 5,

    /// <summary>Frame or message payload exceeds configured limit.</summary>
    PayloadLimit = 6,

    /// <summary>Invalid 64-bit payload length encoding.</summary>
    InvalidPayloadLength = 7,

    /// <summary>§5.4 fragmentation ordering violation.</summary>
    Fragmentation = 8,

    /// <summary>Invalid UTF-8 in Text or Close reason.</summary>
    InvalidUtf8 = 9,

    /// <summary>Invalid Close status code on the wire.</summary>
    InvalidCloseStatus = 10,

    /// <summary>Close frame payload or reason exceeds limits.</summary>
    InvalidClosePayload = 11,
}
