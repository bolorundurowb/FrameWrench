using FrameWrench.Core;
using FrameWrench.Protocol;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class HandshakeHelperTests
{
    [Fact]
    public void GenerateKey_ReturnsBase64Of16Bytes()
    {
        var (keyBytes, keyBase64) = HandshakeHelper.GenerateKey();
        keyBytes.Length.ShouldBe(16);
        Convert.FromBase64String(keyBase64).ShouldBe(keyBytes);
    }

    [Fact]
    public void GenerateKey_TwoCallsProduceDifferentKeys()
    {
        var (_, k1) = HandshakeHelper.GenerateKey();
        var (_, k2) = HandshakeHelper.GenerateKey();
        k1.ShouldNotBe(k2);
    }

    [Fact]
    public void ComputeAcceptValue_MatchesRfc6455TestVector()
    {
        HandshakeHelper.ComputeAcceptValue("dGhlIHNhbXBsZSBub25jZQ==")
            .ShouldBe("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");
    }

    [Fact]
    public void BuildRequest_ContainsMandatoryHeaders()
    {
        var (_, key) = HandshakeHelper.GenerateKey();
        var text = System.Text.Encoding.ASCII.GetString(
            HandshakeHelper.BuildRequest(new Uri("ws://example.com/chat"), key));

        text.ShouldContain("GET /chat HTTP/1.1");
        text.ShouldContain("Host: example.com");
        text.ShouldContain("Upgrade: websocket");
        text.ShouldContain("Connection: Upgrade");
        text.ShouldContain($"Sec-WebSocket-Key: {key}");
        text.ShouldContain("Sec-WebSocket-Version: 13");
        text.ShouldEndWith("\r\n\r\n");
    }

    [Fact]
    public void BuildRequest_IncludesExtraHeaders()
    {
        var (_, key) = HandshakeHelper.GenerateKey();
        var extra = new Dictionary<string, string> { ["Authorization"] = "Bearer token" };
        var text = System.Text.Encoding.ASCII.GetString(
            HandshakeHelper.BuildRequest(new Uri("ws://example.com/"), key, extra));

        text.ShouldContain("Authorization: Bearer token");
    }

    [Fact]
    public void BuildRequest_DefaultPort_OmittedFromHostHeader()
    {
        var (_, key) = HandshakeHelper.GenerateKey();
        var text = System.Text.Encoding.ASCII.GetString(
            HandshakeHelper.BuildRequest(new Uri("ws://example.com/"), key));

        text.ShouldContain("Host: example.com\r\n");
    }

    [Fact]
    public void BuildRequest_NonDefaultPort_IncludedInHostHeader()
    {
        var (_, key) = HandshakeHelper.GenerateKey();
        var text = System.Text.Encoding.ASCII.GetString(
            HandshakeHelper.BuildRequest(new Uri("ws://example.com:9000/"), key));

        text.ShouldContain("Host: example.com:9000\r\n");
    }

    private static Stream ResponseStream(string text) =>
        new MemoryStream(System.Text.Encoding.ASCII.GetBytes(text));

    [Fact]
    public async Task ValidateResponse_Valid101_DoesNotThrow()
    {
        const string accept = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n" +
            "\r\n";

        await Should.NotThrowAsync(
            () => HandshakeHelper.ValidateResponseAsync(
                ResponseStream(response), accept, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateResponse_Non101StatusCode_Throws()
    {
        await Should.ThrowAsync<WebSocketHandshakeException>(
            () => HandshakeHelper.ValidateResponseAsync(
                ResponseStream("HTTP/1.1 400 Bad Request\r\n\r\n"),
                "any",
                CancellationToken.None));
    }

    [Fact]
    public async Task ValidateResponse_WrongAcceptValue_Throws()
    {
        const string expected = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: WRONG==\r\n" +
            "\r\n";

        await Should.ThrowAsync<WebSocketHandshakeException>(
            () => HandshakeHelper.ValidateResponseAsync(
                ResponseStream(response), expected, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateResponse_MissingUpgradeHeader_Throws()
    {
        const string accept = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n" +
            "\r\n";

        await Should.ThrowAsync<WebSocketHandshakeException>(
            () => HandshakeHelper.ValidateResponseAsync(
                ResponseStream(response), accept, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateResponse_EmptyStream_Throws()
    {
        await Should.ThrowAsync<WebSocketHandshakeException>(
            () => HandshakeHelper.ValidateResponseAsync(
                new MemoryStream(Array.Empty<byte>()), "any", CancellationToken.None));
    }
}
