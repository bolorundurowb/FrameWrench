using System.Net;
using System.Net.Sockets;
using System.Text;
using FrameWrench.Core;
using FrameWrench.Protocol;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

/// <summary>
/// Sends a syntactically valid WebSocket Text frame with an invalid UTF-8 payload after a minimal RFC 6455 handshake.
/// </summary>
public sealed class RawInvalidUtf8ReceiveTests : IAsyncLifetime
{
    private TcpListener? _listener;
    private Task? _serverTask;
    private int _port;
    private readonly CancellationTokenSource _cts = new();

    public Task InitializeAsync()
    {
        _port = FreeTcpPort();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
    }

    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private async Task RunServerAsync(CancellationToken ct)
    {
        try
        {
            var tcp = _listener!.AcceptTcpClient();
            tcp.NoDelay = true;
            using (tcp)
            using (var stream = tcp.GetStream())
            {
                var buf = new byte[16 * 1024];
                int n = 0;
                while (n < buf.Length && !ct.IsCancellationRequested)
                {
                    int r = await stream.ReadAsync(buf, n, buf.Length - n, ct).ConfigureAwait(false);
                    if (r == 0)
                        return;
                    n += r;
                    if (n >= 4 &&
                        buf[n - 4] == (byte)'\r' && buf[n - 3] == (byte)'\n' &&
                        buf[n - 2] == (byte)'\r' && buf[n - 1] == (byte)'\n')
                        break;
                }

                var req = Encoding.ASCII.GetString(buf, 0, n);
                var keyLine = req.Split(new[] { "\r\n" }, StringSplitOptions.None)
                    .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
                var key = keyLine.Split(':')[1].Trim();
                var accept = HandshakeHelper.ComputeAcceptValue(key);

                var response =
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
                var respBytes = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(respBytes, 0, respBytes.Length, ct).ConfigureAwait(false);

                var badFrame = BuildServerFrame(FrameOpCode.Text, fin: true, new byte[] { 0xFF });
                await stream.WriteAsync(badFrame, 0, badFrame.Length, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private static byte[] BuildServerFrame(FrameOpCode opCode, bool fin, byte[] payload)
    {
        using var ms = new MemoryStream();
        byte byte0 = (byte)((fin ? 0x80 : 0) | ((byte)opCode & 0x0F));
        ms.WriteByte(byte0);
        ms.WriteByte((byte)payload.Length);
        ms.Write(payload, 0, payload.Length);
        return ms.ToArray();
    }

    [Fact]
    public async Task ReceiveFrameAsync_InvalidUtf8Text_ThrowsWebSocketProtocolException()
    {
        var uri = new Uri($"ws://127.0.0.1:{_port}/");
        await using var client = new FrameWrenchClient(new FrameWrenchOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });

        await client.ConnectAsync(uri);

        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => client.ReceiveFrameAsync(CancellationToken.None));

        ex.Message.ShouldContain("UTF-8");
    }

    [Fact]
    public async Task ReceiveMessageAsync_InvalidUtf8Text_ThrowsWebSocketProtocolException()
    {
        var uri = new Uri($"ws://127.0.0.1:{_port}/");
        await using var client = new FrameWrenchClient(new FrameWrenchOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });

        await client.ConnectAsync(uri);

        var ex = await Should.ThrowAsync<WebSocketProtocolException>(
            () => client.ReceiveMessageAsync(CancellationToken.None));

        ex.Message.ShouldContain("UTF-8");
    }
}
