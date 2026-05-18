using FrameWrench.Internal;

namespace FrameWrench.Core;

/// <summary>
/// Structured, actionable error information attached to every <see cref="FrameWrenchException"/>.
/// </summary>
public sealed class FrameWrenchErrorDetail
{
    /// <summary>Initialises a new <see cref="FrameWrenchErrorDetail"/>.</summary>
    public FrameWrenchErrorDetail(
        string code,
        string title,
        string explanation,
        IReadOnlyDictionary<string, string>? context = null,
        IReadOnlyList<string>? suggestions = null,
        string? rfcSection = null,
        string? rfcUrl = null,
        string? operation = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Error code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Error title is required.", nameof(title));

        Code = code;
        var sanitized = SensitiveDataRedactor.SanitizeDetailFields(
            title,
            explanation ?? string.Empty,
            context);
        Title = sanitized.Title;
        Explanation = sanitized.Explanation;
        Context = sanitized.Context;
        Suggestions = suggestions ?? Array.Empty<string>();
        RfcSection = rfcSection;
        RfcUrl = rfcUrl;
        Operation = operation;
    }

    /// <summary>Stable error identifier, e.g. <c>FW-PROTO-MASKED-SERVER-FRAME</c>.</summary>
    public string Code { get; }

    /// <summary>One-line summary of the failure.</summary>
    public string Title { get; }

    /// <summary>Plain-language explanation of what went wrong.</summary>
    public string Explanation { get; }

    /// <summary>Optional RFC reference label, e.g. <c>RFC 6455 §5.1</c>.</summary>
    public string? RfcSection { get; }

    /// <summary>Optional URL to the relevant RFC section.</summary>
    public string? RfcUrl { get; }

    /// <summary>Diagnostic key/value pairs (secrets must not appear as values).</summary>
    public IReadOnlyDictionary<string, string> Context { get; }

    /// <summary>Actionable steps the caller can take.</summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>API operation during which the error occurred, when known.</summary>
    public string? Operation { get; }
}
