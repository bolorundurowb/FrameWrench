using FrameWrench.Core;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class FrameWrenchClientTests
{
    [Fact]
    public void State_IsNone_OnNewInstance()
    {
        using var client = new FrameWrenchClient();
        client.State.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public void Constructor_NullOptions_UsesDefaults()
    {
        using var client = new FrameWrenchClient(null);
        client.State.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task ConnectAsync_NullUri_Throws_ArgumentNullException()
    {
        await using var client = new FrameWrenchClient();
        await Should.ThrowAsync<ArgumentNullException>(
            () => client.ConnectAsync(null!));
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/")]
    [InlineData("ftp://example.com/")]
    public async Task ConnectAsync_NonWebSocketScheme_Throws_ArgumentException(string uri)
    {
        await using var client = new FrameWrenchClient();
        var ex = await Should.ThrowAsync<ArgumentException>(
            () => client.ConnectAsync(new Uri(uri)));
        ex.Message.ShouldContain("ws");
    }

    [Fact]
    public async Task ConnectAsync_WhenNotInNoneState_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient(new FrameWrenchOptions
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(500)
        });

        try { await client.ConnectAsync(new Uri("ws://127.0.0.1:1/")); }
        catch { }

        client.State.ShouldNotBe(WebSocketState.None);

        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.ConnectAsync(new Uri("ws://127.0.0.1:1/")));

        ex.CurrentState.ShouldNotBe(WebSocketState.None);
        ex.Message.ShouldContain("ConnectAsync");
    }

    [Fact]
    public async Task SendTextAsync_NullText_Throws_ArgumentNullException()
    {
        await using var client = new FrameWrenchClient();
        await Should.ThrowAsync<ArgumentNullException>(
            () => client.SendTextAsync(null!));
    }

    [Fact]
    public async Task SendTextAsync_WhenNotConnected_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient();
        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.SendTextAsync("hello"));
        ex.CurrentState.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task SendBinaryAsync_WhenNotConnected_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient();
        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.SendBinaryAsync(new byte[] { 0x01 }));
        ex.CurrentState.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task SendFrameAsync_NullFrame_Throws_ArgumentNullException()
    {
        await using var client = new FrameWrenchClient();
        await Should.ThrowAsync<ArgumentNullException>(
            () => client.SendFrameAsync((WebSocketFrame)null!));
    }

    [Fact]
    public async Task SendFrameAsync_OpCode_WhenNotConnected_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient();
        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.SendFrameAsync(FrameOpCode.Text, new byte[] { 0x41 }));
        ex.CurrentState.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task SendFrameAsync_Frame_WhenNotConnected_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient();
        var frame = WebSocketFrame.Text("hi");
        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.SendFrameAsync(frame));
        ex.CurrentState.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task PingAsync_WhenNotConnected_Throws_WebSocketStateException()
    {
        await using var client = new FrameWrenchClient();
        var ex = await Should.ThrowAsync<WebSocketStateException>(
            () => client.PingAsync());
        ex.CurrentState.ShouldBe(WebSocketState.None);
    }

    [Fact]
    public async Task CloseAsync_WhenNotOpen_DoesNotThrow()
    {
        await using var client = new FrameWrenchClient();
        await Should.NotThrowAsync(() => client.CloseAsync());
    }

    [Fact]
    public void Dispose_OnFreshClient_DoesNotThrow()
    {
        var client = new FrameWrenchClient();
        Should.NotThrow(client.Dispose);
    }

    [Fact]
    public async Task DisposeAsync_OnFreshClient_DoesNotThrow()
    {
        await using var client = new FrameWrenchClient();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var client = new FrameWrenchClient();
        client.Dispose();
        Should.NotThrow(client.Dispose);
    }
}
