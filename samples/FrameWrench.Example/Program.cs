using FrameWrench.Core;

namespace FrameWrench.Example;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var uri = args.Length > 0
            ? new Uri(args[0])
            : new Uri("wss://echo.websocket.org");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║    FrameWrench - Console example     ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine($"Target URI: {uri}");
        Console.WriteLine();

        var options = new FrameWrenchOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PingTimeout = TimeSpan.FromSeconds(10),
            AutoPing = false,
        };

        await using var client = new FrameWrenchClient(options);

        client.FrameReceived += (_, frame) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [event] Frame received: {frame}.");
            Console.ResetColor();
        };

        PrintStep(2, "Connecting");
        await client.ConnectAsync(uri);
        Console.WriteLine($"  Connection state: {client.State}");

        PrintStep(3, "Send Text frame and receive echo");
        const string textMessage = "Hello from FrameWrench!";
        await client.SendTextAsync(textMessage);
        Console.WriteLine($"  Sent: \"{textMessage}\"");

        var echo = await client.ReceiveMessageAsync();
        Console.WriteLine($"  Echo: \"{echo.GetText()}\"");
        Console.WriteLine($"  Fragments: {echo.Frames.Count}");

        PrintStep(4, "Explicit Ping and Pong (latency)");
        var pingPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        Console.WriteLine($"  Sending Ping with payload (hex): {BytesToHex(pingPayload)}");

        var (pongReceived, roundtrip) = await client.PingAsync(
            payload: pingPayload,
            timeout: TimeSpan.FromSeconds(10));

        if (pongReceived)
            Console.WriteLine($"  Pong received. Round-trip time: {roundtrip.TotalMilliseconds:0.00} ms.");
        else
            Console.WriteLine("  Pong was not received (timed out).");

        PrintStep(5, "Fragmented message: send and receive by frame");

        var part1Bytes = System.Text.Encoding.UTF8.GetBytes("Fragmented ");
        var part2Bytes = System.Text.Encoding.UTF8.GetBytes("Hello!");

        Console.WriteLine("  Sending frame 1/2: Text opcode, FIN=false (\"Fragmented \")");
        await client.SendFrameAsync(FrameOpCode.Text, part1Bytes, isFinal: false);

        Console.WriteLine("  Sending frame 2/2: Continuation opcode, FIN=true (\"Hello!\")");
        await client.SendFrameAsync(FrameOpCode.Continuation, part2Bytes, isFinal: true);

        Console.WriteLine("  Receiving via GetFrameStream (one frame at a time):");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var collected = new List<WebSocketFrame>();

        await foreach (var frame in client.GetFrameStream(cts.Token))
        {
            if (frame.IsControl)
            {
                Console.WriteLine($"    [control] {frame}");
                continue;
            }

            Console.WriteLine($"    [data]    {frame}");
            collected.Add(frame);

            if (frame.IsFinal) break;
        }

        var reassembled = string.Concat(
            collected.Select(f => System.Text.Encoding.UTF8.GetString(f.Payload.ToArray())));
        Console.WriteLine($"  Reassembled: \"{reassembled}\"");

        PrintStep(6, "Send raw Binary frame with SendFrameAsync");
        var binaryPayload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await client.SendFrameAsync(FrameOpCode.Binary, binaryPayload);
        Console.WriteLine($"  Sent binary: {BytesToHex(binaryPayload)}");

        var binMsg = await client.ReceiveMessageAsync();
        Console.WriteLine($"  Echo binary: {BytesToHex(binMsg.Payload.ToArray())}");

        PrintStep(7, "Close handshake");
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Example complete.");
        Console.WriteLine($"  Final connection state: {client.State}");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("All steps completed successfully.");
        Console.ResetColor();
    }

    private static void PrintStep(int n, string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"── Step {n}: {title}");
        Console.ResetColor();
    }

    private static string BytesToHex(byte[] bytes) =>
        string.Join(" ", bytes.Select(b => $"{b:X2}"));
}
