using FrameWrench;
using FrameWrench.Core;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// FrameWrench Example Console Application
//
// Demonstrates:
//   1. Connecting to a WebSocket echo server
//   2. Sending a Text frame and receiving the echo
//   3. Sending a Ping with a custom payload and measuring round-trip time
//   4. Sending a fragmented message (two frames) and reading it back at the
//      frame level using GetFrameStream
//   5. Printing each received frame's opcode
//   6. Clean close handshake
//
// Default target: wss://echo.websocket.org  (or pass a custom URI as argv[0])
// ─────────────────────────────────────────────────────────────────────────────

namespace FrameWrench.Example;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // ── 0. Resolve URI ──────────────────────────────────────────────────
        var uri = args.Length > 0
            ? new Uri(args[0])
            : new Uri("wss://echo.websocket.org");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║     FrameWrench – Console Example    ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine($"Target: {uri}");
        Console.WriteLine();

        // ── 1. Create logger + client ───────────────────────────────────────
        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var options = new FrameWrenchOptions
        {
            ConnectTimeout  = TimeSpan.FromSeconds(15),
            PingTimeout     = TimeSpan.FromSeconds(10),
            AutoPing        = false,   // We'll call PingAsync explicitly
        };

        await using var client = new FrameWrenchClient(options, loggerFactory.CreateLogger<FrameWrenchClient>());

        // ── Register frame event (prints every received frame opcode) ────────
        client.FrameReceived += (_, frame) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [event] Frame received: {frame}");
            Console.ResetColor();
        };

        // ── 2. Connect ──────────────────────────────────────────────────────
        PrintStep(2, "Connecting");
        await client.ConnectAsync(uri);
        Console.WriteLine($"  State: {client.State}");

        // ── 3. Send a text frame + receive echo ──────────────────────────────
        PrintStep(3, "Send text frame & receive echo");
        const string textMessage = "Hello from FrameWrench!";
        await client.SendTextAsync(textMessage);
        Console.WriteLine($"  Sent: \"{textMessage}\"");

        var echo = await client.ReceiveMessageAsync();
        Console.WriteLine($"  Echo: \"{echo.GetText()}\"");
        Console.WriteLine($"  Fragments: {echo.Frames.Count}");

        // ── 4. Explicit Ping / Pong with latency measurement ─────────────────
        PrintStep(4, "Explicit Ping → Pong with latency");
        var pingPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        Console.WriteLine($"  Sending Ping with payload: {BytesToHex(pingPayload)}");

        var (pongReceived, roundtrip) = await client.PingAsync(
            payload: pingPayload,
            timeout: TimeSpan.FromSeconds(10));

        if (pongReceived)
            Console.WriteLine($"  ✓ Pong received! Round-trip: {roundtrip.TotalMilliseconds:0.00} ms");
        else
            Console.WriteLine("  ✗ Pong NOT received (timeout)");

        // ── 5. Fragmented message – send two frames, read at frame level ─────
        PrintStep(5, "Fragmented message send + frame-level receive");

        var part1Bytes = System.Text.Encoding.UTF8.GetBytes("Fragmented ");
        var part2Bytes = System.Text.Encoding.UTF8.GetBytes("Hello!");

        Console.WriteLine("  Sending frame 1/2: Text opcode, FIN=false (\"Fragmented \")");
        await client.SendFrameAsync(FrameOpCode.Text,         part1Bytes, isFinal: false);

        Console.WriteLine("  Sending frame 2/2: Continuation opcode, FIN=true (\"Hello!\")");
        await client.SendFrameAsync(FrameOpCode.Continuation, part2Bytes, isFinal: true);

        // Collect frames via the low-level async stream until we complete the message
        Console.WriteLine("  Receiving via GetFrameStream (frame by frame):");

        using var cts       = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var       collected = new List<WebSocketFrame>();

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

        // ── 6. Low-level frame send using SendFrameAsync directly ─────────────
        PrintStep(6, "Send raw Binary frame using SendFrameAsync");
        var binaryPayload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        await client.SendFrameAsync(FrameOpCode.Binary, binaryPayload);
        Console.WriteLine($"  Sent binary: {BytesToHex(binaryPayload)}");

        var binMsg = await client.ReceiveMessageAsync();
        Console.WriteLine($"  Echo binary: {BytesToHex(binMsg.Payload.ToArray())}");

        // ── 7. Close ─────────────────────────────────────────────────────────
        PrintStep(7, "Close handshake");
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Example complete");
        Console.WriteLine($"  Final state: {client.State}");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("All steps completed successfully.");
        Console.ResetColor();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
