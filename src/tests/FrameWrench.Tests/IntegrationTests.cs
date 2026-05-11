using System.Net;
using System.Net.WebSockets;
using FrameWrench.Core;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public sealed class IntegrationTests : IAsyncLifetime
{
    private HttpListener?            _listener;
    private CancellationTokenSource  _serverCts = new CancellationTokenSource();
    private Task?                    _serverTask;
    private int                      _port;

    public async Task InitializeAsync()
    {
        _port     = FreeTcpPort();
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
                var ws    = wsCtx.WebSocket;
                var buf   = new byte[64 * 1024];

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

    private Uri ServerUri => new Uri($"ws://localhost:{_port}/");

    private static FrameWrenchClient NewClient() =>
        new FrameWrenchClient(new FrameWrenchOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PingTimeout    = TimeSpan.FromSeconds(5),
        });

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

        var (received, roundtrip) = await client.PingAsync(
            timeout: TimeSpan.FromSeconds(5));

        received.ShouldBeTrue("The server must respond to Ping with a Pong.");
        roundtrip.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        await client.CloseAsync();
    }

    [Fact]
    public async Task PingAsync_CustomPayload_CorrelatesWithPong()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        var (received, _) = await client.PingAsync(
            payload: new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            timeout: TimeSpan.FromSeconds(5));

        received.ShouldBeTrue();

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

        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var       frames = new List<WebSocketFrame>();

        await foreach (var frame in client.GetFrameStream(cts.Token))
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
        await using var client    = NewClient();
        var             gotFrames = new List<WebSocketFrame>();
        client.FrameReceived += (_, f) => gotFrames.Add(f);

        await client.ConnectAsync(ServerUri);
        await client.SendTextAsync("event test");
        await Task.Delay(300);

        gotFrames.ShouldNotBeEmpty();

        await client.CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_TransitionsStateAwayFromOpen()
    {
        await using var client = NewClient();
        await client.ConnectAsync(ServerUri);

        await client.CloseAsync(global::FrameWrench.Core.WebSocketCloseStatus.NormalClosure, "done");

        client.State.ShouldNotBe(global::FrameWrench.Core.WebSocketState.Open);
    }
}
