using FrameWrench.Core;
using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class SensitiveDataRedactorTests
{
    [Fact]
    public void FrameWrenchErrorDetail_SensitiveContextKey_Redacted()
    {
        var detail = new FrameWrenchErrorDetail(
            "FW-TEST",
            "test",
            "explanation",
            new Dictionary<string, string> { ["Authorization"] = "Bearer x" });

        detail.Context["Authorization"].ShouldBe(SensitiveDataRedactor.RedactedPlaceholder);
        detail.Title.ShouldBe("test");
    }

    [Fact]
    public void SanitizeText_BearerInExplanation_Redacted()
    {
        var sanitized = SensitiveDataRedactor.SanitizeText("Token was Bearer abc123.def");
        sanitized.ShouldBe($"Token was {SensitiveDataRedactor.RedactedPlaceholder}");
    }

    [Fact]
    public void HandshakeAcceptMismatch_StillIncludesTruncatedHashes()
    {
        var ex = FrameWrenchErrors.HandshakeAcceptMismatch(
            "expected-hash-value-32-chars-long!!",
            "actual-hash-value-32-chars-long!!!!",
            "HTTP/1.1 101 Switching Protocols");

        ex.Message.ShouldContain("expectedAccept");
        ex.Message.ShouldContain("receivedAccept");
        ex.Detail.Context["expectedAccept"].ShouldContain("expected-hash");
        ex.Detail.Context["receivedAccept"].ShouldContain("actual-hash");
    }
}
