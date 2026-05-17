using System.Net;
using System.Net.WebSockets;
using FrameWrench.Core;
using Shouldly;
using Xunit;
using WebSocketState = FrameWrench.Core.WebSocketState;

namespace FrameWrench.Tests;

public sealed class IntegrationTests : IAsyncLifetime
{
    private HttpListener? _listener;
    private CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = FreeTcpPort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _serverTask = RunEchoServerAsync(_serverCts.Token);
        await Task.Yield();
    }

    public async Task DisposeAsync()
    {
        _serverCts.Cancel();
        try { _listener?.Stop(); } catch { }
        if (_serverTask is not null)
            try { await _serverTask; } catch { }
    }

    private async Task RunEchoServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { return; }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                continue;
            }

            _ = Task.Run(async () =>
            {
                var wsCtx = await ctx.AcceptWebSocketAsync(null);
                var ws = wsCtx.WebSocket;
                var buf = new byte[64 * 1024];

                while (ws.State == System.Net.WebSockets.WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    }
                    catch { break; }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", ct); }
                        catch { }
                        break;
                    }

                    try
                    {
                        await ws.SendAsync(
                            new ArraySegment<byte>(buf, 0, result.Count),
                            result.MessageType,
                            result.EndOfMessage,
                            ct);
                    }
                    catch { break; }
                }
            }, ct);
        }
    }

    private static int FreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private Uri ServerUri => new($"ws://localhost:{_port}/");

    private static FrameWrenchClient NewClient() =>
        new(FrameWrenchOptions.Create()
            .WithConnectTimeout(TimeSpan.FromSeconds(10))
            .WithPingTimeout(TimeSpan.FromSeconds(5))
            .Build());

    [Fact]
    public async Task Connect_OpensSuccessfully()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);
        client.State.ShouldBe(global::FrameWrench.Core.WebSocketState.Open);
        await client.CloseAsync();
    }

    [Fact]
    public async Task SendText_EchoedBack_MatchesOriginal()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.SendTextAsync("Hello, FrameWrench!");
        var response = await client.ReceiveMessageAsync();

        response.MessageType.ShouldBe(FrameOpCode.Text);
        response.GetText().ShouldBe("Hello, FrameWrench!");

        await client.CloseAsync();
    }

    [Fact]
    public async Task SendBinary_EchoedBack_MatchesOriginal()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        var data = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        await client.SendBinaryAsync(data);
        var response = await client.ReceiveMessageAsync();

        response.MessageType.ShouldBe(FrameOpCode.Binary);
        response.Payload.ToArray().ShouldBe(data);

        await client.CloseAsync();
    }

    [Fact]
    public async Task PingAsync_ReceivesPong_WithCorrelation()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        var pingResult = await client.PingAsync(
            timeout: TimeSpan.FromSeconds(5));

        pingResult.PongReceived.ShouldBeTrue("The server must respond to Ping with a Pong.");
        pingResult.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        await client.CloseAsync();
    }

    [Fact]
    public async Task PingAsync_CustomPayload_CorrelatesWithPong()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        var pingResult = await client.PingAsync(
            payload: new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            timeout: TimeSpan.FromSeconds(5));

        pingResult.PongReceived.ShouldBeTrue();

        await client.CloseAsync();
    }

    [Fact]
    public async Task FragmentedMessage_ReassembledCorrectly_ViaMessageApi()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.SendFrameAsync(FrameOpCode.Text,
            System.Text.Encoding.UTF8.GetBytes("Hello, "), isFinal: false);
        await client.SendFrameAsync(FrameOpCode.Continuation,
            System.Text.Encoding.UTF8.GetBytes("World!"), isFinal: true);

        var response = await client.ReceiveMessageAsync();
        response.GetText().ShouldBe("Hello, World!");

        await client.CloseAsync();
    }

    [Fact]
    public async Task GetFrameStream_DeliversFrames()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.SendTextAsync("stream test");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var frames = new List<WebSocketFrame>();

        await foreach (var frame in client.ReceiveFramesAsync(cts.Token))
        {
            if (frame.IsControl) continue;
            frames.Add(frame);
            if (frame.IsFinal) break;
        }

        frames.ShouldNotBeEmpty();
        var text = string.Concat(
            frames.Select(f => System.Text.Encoding.UTF8.GetString(f.Payload.ToArray())));
        text.ShouldBe("stream test");

        await client.CloseAsync();
    }

    [Fact]
    public async Task FrameReceived_EventFires_ForEachFrame()
    {
        await using var client = NewClient();
        var gotNonControl = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.FrameReceived += (_, f) =>
        {
            if (!f.IsControl && !gotNonControl.Task.IsCompleted)
                gotNonControl.TrySetResult(true);
        };

        await client.ConnectAsync(ServerUri);
        await client.SendTextAsync("event test");

        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        var winner = await Task.WhenAny(gotNonControl.Task, timeout);
        winner.ShouldBe(gotNonControl.Task, "Expected FrameReceived to deliver a non-control frame.");

        await client.CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_TransitionsStateAwayFromOpen()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.CloseAsync(global::FrameWrench.Core.WireCloseStatus.NormalClosure, "done");

        client.State.ShouldNotBe(global::FrameWrench.Core.WebSocketState.Open);
    }

    [Fact]
    public async Task SendFrame_TextFinal_InvalidUtf8_ThrowsBeforeWrite()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await Should.ThrowAsync<WebSocketProtocolException>(() =>
            client.SendFrameAsync(FrameOpCode.Text, new byte[] { 0xFF }, isFinal: true));

        await client.CloseAsync();
    }

    [Fact]
    public async Task SendTextAsync_NullText_Throws_ArgumentNullException_WhenConnected()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await Should.ThrowAsync<ArgumentNullException>(
            () => client.SendTextAsync(null!));

        await client.CloseAsync();
    }

    [Fact]
    public async Task SendFrameAsync_NullFrame_Throws_ArgumentNullException_WhenConnected()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await Should.ThrowAsync<ArgumentNullException>(
            () => client.SendFrameAsync((WebSocketFrame)null!));

        await client.CloseAsync();
    }

    [Fact]
    public async Task PingAsync_PayloadOver125Bytes_Throws_ArgumentException()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        var oversized = new byte[126];
        var ex = await Should.ThrowAsync<ArgumentException>(
            () => client.PingAsync(payload: oversized));

        ex.Message.ShouldContain("FW-ARG-PING-PAYLOAD");

        await client.CloseAsync();
    }

    [Fact]
    public async Task ReceiveMessageAsync_AfterClose_Throws_FrameWrenchException()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);
        await client.CloseAsync();

        await Should.ThrowAsync<FrameWrenchException>(
            () => client.ReceiveMessageAsync());
    }

    [Fact]
    public async Task ReceiveMessageAsync_MaxMessagePayloadExceeded_Throws()
    {
        var options = FrameWrenchOptions.Create()
            .WithConnectTimeout(TimeSpan.FromSeconds(10))
            .WithMaxMessagePayloadBytes(3)
            .Build();

        await using var client = new FrameWrenchClient(options);
        await client.ConnectAsync(ServerUri);

        await client.SendTextAsync("Hello");

        await Should.ThrowAsync<WebSocketProtocolException>(
            () => client.ReceiveMessageAsync());
    }

    [Fact]
    public async Task Dispose_WhenOpen_TransitionsStateToNonOpen()
    {
        var client = NewClient();
        await client.ConnectAsync(ServerUri);

        client.Dispose();

        client.State.ShouldNotBe(WebSocketState.Open);
        client.State.ShouldNotBe(WebSocketState.None);
    }

    [Fact]
    public async Task DisposeAsync_WhenOpen_TransitionsStateToNonOpen()
    {
        var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.DisposeAsync();

        client.State.ShouldNotBe(WebSocketState.Open);
        client.State.ShouldNotBe(WebSocketState.None);
    }

    [Fact]
    public async Task State_IsOpen_AfterSuccessfulConnect()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);
        client.State.ShouldBe(global::FrameWrench.Core.WebSocketState.Open);
        await client.CloseAsync();
    }

    [Fact]
    public async Task SendBinaryAsync_EmptyPayload_EchoedBack()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.SendBinaryAsync(ReadOnlyMemory<byte>.Empty);
        var response = await client.ReceiveMessageAsync();

        response.MessageType.ShouldBe(FrameOpCode.Binary);
        response.Payload.Length.ShouldBe(0);

        await client.CloseAsync();
    }

    [Fact]
    public async Task SendTextAsync_UnicodeText_EchoedBack()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        const string text = "こんにちは世界";
        await client.SendTextAsync(text);
        var response = await client.ReceiveMessageAsync();

        response.MessageType.ShouldBe(FrameOpCode.Text);
        response.GetText().ShouldBe(text);

        await client.CloseAsync();
    }

    [Fact]
    public async Task ReceiveMessageAsync_MultipleMessages_InOrder()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.SendTextAsync("first");
        await client.SendTextAsync("second");
        await client.SendTextAsync("third");

        var m1 = await client.ReceiveMessageAsync();
        var m2 = await client.ReceiveMessageAsync();
        var m3 = await client.ReceiveMessageAsync();

        m1.GetText().ShouldBe("first");
        m2.GetText().ShouldBe("second");
        m3.GetText().ShouldBe("third");

        await client.CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_CustomStatus_TransitionsStateToNonOpen()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.CloseAsync(
            global::FrameWrench.Core.WireCloseStatus.GoingAway,
            "shutting down");

        client.State.ShouldNotBe(global::FrameWrench.Core.WebSocketState.Open);
    }

    [Fact]
    public async Task CloseAsync_CalledTwice_DoesNotThrow()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.CloseAsync();

        await Should.NotThrowAsync(() => client.CloseAsync());
    }
}
