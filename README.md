<div align="center">
  <img
    src="https://raw.githubusercontent.com/bolorundurowb/FrameWrench/refs/heads/master/assets/frame-wrench-logo.png"
    alt="FrameWrench logo"  />
</div>

# FrameWrench

[![Build, Test & Coverage](https://github.com/bolorundurowb/FrameWrench/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/bolorundurowb/FrameWrench/actions/workflows/build-and-test.yml) [![codecov](https://codecov.io/gh/bolorundurowb/FrameWrench/graph/badge.svg?token=poFOTCdIj8)](https://codecov.io/gh/bolorundurowb/FrameWrench) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) ![NuGet Version](https://img.shields.io/nuget/v/FrameWrench)

**A lightweight, client-only RFC 6455 WebSocket library for .NET Framework 4.6.2+, .NET Framework 4.8, and .NET Standard 2.0.**

FrameWrench gives you *explicit, frame-level control* over every WebSocket interaction – including raw Ping/Pong handling, fragmented messages, and direct access to individual frames - while still offering a convenient message-level API when you don't need that control.

> ⚠️ **AI Disclosure:** This project was developed with the assistance of generative AI. All code, architecture decisions, and documentation were reviewed and refined as part of the development process.

## Why FrameWrench?

### The Gap in the .NET Ecosystem

The built-in `System.Net.WebSockets.ClientWebSocket` introduced in .NET 4.5 abstracts away the frame layer almost entirely. It does not:

- Expose individual frames - you only get reassembled messages.
- Let you send or intercept Ping/Pong frames directly.
- Allow manual control over message fragmentation.
- Provide a frame-level event model.

For applications targeting .NET Framework 4.6.2 or 4.8 that need **frame-level control** - such as those implementing custom heartbeat logic, real-time latency measurement via explicit Ping/Pong correlation, or streaming large payloads as controlled fragments - `ClientWebSocket` simply is not the right tool.

### Why not an existing open-source library?

As of early 2025 there is no maintained, open-source WebSocket *client* library for pre-.NET 6 targets that:

1. **Provides a frame-level API** (Ping, Pong, Continuation, explicit fragmentation control)
2. **Targets .NET Framework 4.6.2 / 4.8** with no platform-specific dependencies
3. Is **actively maintained** and free of external WebSocket-specific NuGet dependencies

- **WebSocketSharp** - unmaintained since ~2022; server-centric; limited frame API.
- **Fleck / SuperWebSocket** - server libraries, not client.
- **SignalR** - protocol layer on top of WebSockets; no raw frame access.
- **websocket-client (dotnet-websocket-client)** - message-level only; no frame events.

FrameWrench fills this gap: a clean, minimal RFC 6455 *core client* (no extensions) that treats frames as first-class citizens, with strict close/UTF-8 validation and actionable errors.

> **Upgrading from 1.x?** See [MIGRATION.md](MIGRATION.md) for breaking changes in 2.0.

### Who Needs FrameWrench?

- Legacy .NET Framework 4.6.2/4.8 applications requiring frame-level WebSocket control
- Real-time latency measurement via explicit Ping/Pong with correlation
- Custom message fragmentation logic (e.g., implementing a proprietary streaming protocol on top of WebSockets)
- Low-level protocol implementations (e.g., building a custom trading system, game server, or telemetry pipeline)

### Who Doesn't Need It?

- **Modern .NET 6+ applications** → use built-in `ClientWebSocket` or higher-level libraries (SignalR, gRPC)
- **Message-oriented applications** → SignalR, Blazor, or WebSocket libraries with message-level APIs
- **Compression support needed** → alternative libraries with extension support
- **Server implementations** → this is client-only; server use cases are out of scope


## Features

| Feature                  | Detail                                                                                                                                                                                                                                     |
|--------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Frame-level API**      | Send and receive individual frames for all opcodes                                                                                                                                                                                         |
| **Explicit Ping/Pong**   | `PingAsync` with payload correlation and RTT measurement                                                                                                                                                                                   |
| **Fragmented messages**  | Send multi-frame messages; receive as individual frames or reassembled                                                                                                                                                                     |
| **Message-level API**    | `ReceiveMessageAsync` / `SendTextAsync` / `SendBinaryAsync` for simple use cases                                                                                                                                                           |
| **Async event model**    | `FrameReceived` event fires for every incoming frame                                                                                                                                                                                       |
| **Async streaming**      | `ReceiveFramesAsync()` returns `IAsyncEnumerable<WebSocketFrame>`                                                                                                                                                                          |
| **Actionable errors**    | Stable `ErrorCode`, RFC links, context, and `help:` suggestions on every failure                                                                                                                                                           |
| **TLS support**          | Full `wss://` with configurable `SslProtocols` and cert validation callback                                                                                                                                                                |
| **Auto-Ping (opt-in)**   | Configurable keepalive via `FrameWrenchOptions.AutoPing`                                                                                                                                                                                   |
| **Zero WS dependencies** | No WebSocket-specific NuGet packages; only Microsoft BCL shims (`System.Memory`, `System.Threading.Channels`, and others), with **per-target** direct dependencies so **net48** / **netstandard2.0** do not pull packages they do not need |
| **Logger-agnostic**      | No `ILogger` or logging package dependency - use NLog, Serilog, `Trace`, or your own wrappers around `FrameReceived` / exceptions                                                                                                          |
| **Target frameworks**    | `net462`, `net48`, `netstandard2.0`                                                                                                                                                                                                        |


## Installation

**Recommended:** install **FrameWrench** from [NuGet](https://www.nuget.org/packages/FrameWrench) so you get a signed, versioned package and transitive dependencies resolved automatically.

### CLI

```bash
dotnet add package FrameWrench
```

To pin a specific version:

```bash
dotnet add package FrameWrench --version 2.0.0
```

### Project file

```xml
<ItemGroup>
  <PackageReference Include="FrameWrench" Version="2.0.0" />
</ItemGroup>
```

Use the latest **stable** version from the NuGet gallery (the value above is illustrative).

### From source (contributors or local builds)

Clone the repository and reference the library project directly:

```xml
<ProjectReference Include="../path/to/FrameWrench/src/FrameWrench/FrameWrench.csproj" />
```

Or produce a local package:

```bash
cd src/FrameWrench
dotnet pack -c Release
```

Then consume the resulting `.nupkg` via a [local NuGet feed](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds) or `dotnet add package` with a `--source` path.


## Quick Start

Use one `FrameWrenchClient` per WebSocket connection; after a close, create a new instance to reconnect (unlike `HttpClient`, instances are not pooled for reuse).

Public echo WebSocket URLs change over time; verify the host or use a local server (see integration tests and the sample) before relying on a specific address.

```csharp
using FrameWrench;
using FrameWrench.Core;

await using var client = new FrameWrenchClient();
ConnectResult connect = await client.ConnectAsync(new Uri("wss://echo.websocket.org"));
// connect.SelectedSubProtocol is set when the server picks a subprotocol

// Send a text message
await client.SendTextAsync("Hello, World!");

// Receive the echo
var message = await client.ReceiveMessageAsync();
Console.WriteLine(message.GetText()); // "Hello, World!"

var close = await client.CloseAsync(WireCloseStatus.NormalClosure, "bye");
// close.HandshakeCompleted, close.FinalState
```


## API Reference

### FrameWrenchClient

The main class. Create one per connection.

```csharp
var client = new FrameWrenchClient(options);
// or: new FrameWrenchClient() — uses FrameWrenchOptions.Default
```

#### Connection

```csharp
// Connect (ws:// or wss://) — returns ConnectResult
ConnectResult connect = await client.ConnectAsync(
    new Uri("ws://localhost:8080/ws"),
    cancellationToken);
string? subProtocol = connect.SelectedSubProtocol;

// Current state
WebSocketState state = client.State; // None | Connecting | Open | CloseSent | CloseReceived | Closed | Aborted
```

#### Frame-Level Send

```csharp
// Send any opcode with explicit FIN bit
await client.SendFrameAsync(FrameOpCode.Text,   payload, isFinal: true);
await client.SendFrameAsync(FrameOpCode.Binary, payload, isFinal: true);

// Send a pre-built frame
var frame = WebSocketFrame.Binary(data, isFinal: false); // first fragment
await client.SendFrameAsync(frame);

// Continue the fragment
await client.SendFrameAsync(FrameOpCode.Continuation, moreData, isFinal: true);
```

#### Frame-Level Receive

```csharp
// Pull the next frame (one at a time)
WebSocketFrame frame = await client.ReceiveFrameAsync(ct);
Console.WriteLine(frame.OpCode);    // Text | Binary | Continuation | Ping | Pong | Close
Console.WriteLine(frame.IsFinal);   // true for the last (or only) fragment
Console.WriteLine(frame.Payload.Length);

// Stream all frames asynchronously (preferred)
await foreach (var f in client.ReceiveFramesAsync(ct))
{
    Console.WriteLine($"Received: {f}");
    if (f.OpCode == FrameOpCode.Close) break;
}

// GetFrameStream is obsolete; use ReceiveFramesAsync
```

#### Event-Driven Receive

```csharp
// Fires on the receive-pump thread for every incoming frame
client.FrameReceived += (sender, frame) =>
{
    Console.WriteLine($"[event] {frame.OpCode} len={frame.Payload.Length}");
};
```

#### Message-Level API (Convenience)

```csharp
// Send
await client.SendTextAsync("hello");
await client.SendBinaryAsync(bytes);

// Receive (reassembles fragmented messages automatically)
WebSocketMessage msg = await client.ReceiveMessageAsync(ct);
Console.WriteLine(msg.MessageType);     // FrameOpCode.Text or .Binary
Console.WriteLine(msg.GetText());       // UTF-8 decoded text
Console.WriteLine(msg.Payload.Length);  // total reassembled length
Console.WriteLine(msg.Frames.Count);    // number of fragments
```

#### Ping / Pong

```csharp
// Explicit Ping with payload correlation and RTT measurement
PingResult ping = await client.PingAsync(
    payload: new byte[] { 0x01, 0x02, 0x03, 0x04 },
    timeout: TimeSpan.FromSeconds(5),
    ct: cancellationToken);

if (ping.PongReceived)
    Console.WriteLine($"Pong in {ping.Elapsed.TotalMilliseconds:0.0} ms");
else
    Console.WriteLine("Pong not received within timeout");

// Send a raw Pong (e.g., unsolicited heartbeat)
await client.SendFrameAsync(WebSocketFrame.Pong(payload));
```

#### Close

Use `WireCloseStatus` for registered status codes (RFC 6455 §7.4.1). For application-defined codes (3000–4999), build a frame with `WebSocketFrame.Close(4000, reason)` and send it with `SendFrameAsync`, or rely on the library to echo the peer’s code when you receive their Close first.

```csharp
CloseResult result = await client.CloseAsync(
    WireCloseStatus.NormalClosure,
    reason: "done",
    ct: cancellationToken);

// result.HandshakeCompleted — peer echoed Close within CloseHandshakeTimeout
// result.FinalState
```


## Configuration – FrameWrenchOptions

```csharp
var options = FrameWrenchOptions.Create()
    .WithConnectTimeout(TimeSpan.FromSeconds(30))
    .WithExtraHeader("Authorization", "Bearer ...")
    .WithSubProtocol("chat")
    .WithMaxFramePayloadBytes(64 * 1024 * 1024)
    .WithMaxMessagePayloadBytes(64 * 1024 * 1024)
    .WithPingTimeout(TimeSpan.FromSeconds(10))
    .WithCloseHandshakeTimeout(TimeSpan.FromSeconds(5))
    .WithFailOnInvalidIncomingUtf8(true)   // default: validate inbound Text/Close UTF-8
    .WithValidateOutgoingMessages(true)    // default: validate outbound fragmentation/UTF-8
    .Build();
    // TLS: .WithSslProtocols(...), .WithRemoteCertificateValidationCallback(...) for wss://
    // .WithSingleFrameConsumer(true) — only one ReceiveFramesAsync / ReceiveFrameAsync at a time
```

Options are **immutable** after `Build()`. `ExtraHeaders` cannot override reserved handshake headers (`Upgrade`, `Connection`, `Sec-WebSocket-Key`, and others); names and values must not contain CR, LF, or NUL.


## WebSocketFrame

All frames sent and received are represented as `WebSocketFrame`. Factory methods make construction easy:

```csharp
// Data frames
WebSocketFrame.Text("hello");                        // FIN=true
WebSocketFrame.Text("chunk", isFinal: false);        // first fragment
WebSocketFrame.Binary(bytes);                        // FIN=true
WebSocketFrame.Continuation(moreBytes, isFinal: true);

// Control frames
WebSocketFrame.Ping(payload);    // optional payload ≤ 125 bytes
WebSocketFrame.Pong(payload);
WebSocketFrame.Close(WireCloseStatus.NormalClosure, "bye");
WebSocketFrame.Close(4000, "app-specific");  // 3000–4999 application codes

// Reading a Text frame
var text = frame.GetTextPayload();

// Reading a Close frame
CloseFrameInfo close = frame.GetCloseInfo();
// close.StatusCode — raw wire code (always set when payload ≥ 2 bytes)
// close.Status     — WireCloseStatus when registered; null for 3000–4999
// close.Reason
```

`WebSocketCloseStatus` (e.g. `NoStatusReceived`, `AbnormalClosure`) is for **local** connection state only — never send those values in a Close frame.


## Fragmentation Guide

RFC 6455 §5.4 allows splitting large messages across multiple frames. FrameWrench gives you full control over this.

### Sending a fragmented message

```csharp
// Fragment 1: use the data opcode (Text or Binary), FIN=false
await client.SendFrameAsync(FrameOpCode.Text,
    Encoding.UTF8.GetBytes("Part one "),
    isFinal: false);

// Fragment 2+: use Continuation opcode, FIN=false for intermediate
await client.SendFrameAsync(FrameOpCode.Continuation,
    Encoding.UTF8.GetBytes("Part two "),
    isFinal: false);

// Last fragment: Continuation opcode, FIN=true
await client.SendFrameAsync(FrameOpCode.Continuation,
    Encoding.UTF8.GetBytes("Part three."),
    isFinal: true);
```

### Receiving fragments at the frame level

```csharp
await foreach (var frame in client.ReceiveFramesAsync(ct))
{
    // Control frames (Ping, Pong) may be interleaved - handle them
    if (frame.IsControl) continue;

    // Accumulate fragment payloads
    buffer.AddRange(frame.Payload.ToArray());

    if (frame.IsFinal)
    {
        var fullText = Encoding.UTF8.GetString(buffer.ToArray());
        Console.WriteLine("Complete message: " + fullText);
        buffer.Clear();
    }
}
```

### Automatic reassembly (convenience)

```csharp
// ReceiveMessageAsync handles all of this for you
var msg = await client.ReceiveMessageAsync(ct);
Console.WriteLine(msg.GetText());
```


## Ping/Pong Guide

### How correlation works

`PingAsync` generates (or uses your provided) payload, registers a waiter in a FIFO queue keyed by the Base64 representation of that payload, sends the Ping frame, and awaits a `TaskCompletionSource`. The receive pump matches each Pong’s echoed application data to that key and completes the oldest outstanding waiter, so concurrent pings with the same payload still correlate correctly. If no Pong arrives within the timeout, `PingResult.PongReceived` is `false`.

```csharp
// The payload is echoed verbatim in the Pong - use it as a correlation key
PingResult ping = await client.PingAsync(
    payload: Encoding.UTF8.GetBytes("probe-1"),
    timeout: TimeSpan.FromSeconds(5));
```

### Manual Pong handling

If you don't use `PingAsync`, you can still send Ping frames and handle Pongs yourself:

```csharp
await client.SendFrameAsync(WebSocketFrame.Ping(myCorrelationBytes));

await foreach (var frame in client.ReceiveFramesAsync(ct))
{
    if (frame.OpCode == FrameOpCode.Pong)
    {
        // frame.Payload contains the echo of your ping payload
        HandlePong(frame.Payload);
    }
}
```

### Unsolicited Pong (keepalive)

```csharp
// Unidirectional heartbeat - no Ping required first
await client.SendFrameAsync(WebSocketFrame.Pong());
```

### Auto-Ping

```csharp
var opts = FrameWrenchOptions.Create()
    .WithAutoPing(true)
    .WithKeepAliveInterval(TimeSpan.FromSeconds(20))
    .WithPingTimeout(TimeSpan.FromSeconds(8))
    .Build();
// If the server doesn't respond within PingTimeout, CloseAsync is called automatically.
```


## Thread Safety

| Operation               | Notes                                                                                                                                                                                                                                                                                                                                                                                                |
|-------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Sending frames**      | Concurrent sends are serialised through an internal `SemaphoreSlim`. Multiple threads may call `SendFrameAsync` concurrently; frames are written atomically.                                                                                                                                                                                                                                         |
| **Receiving frames**    | A single background pump reads frames from the wire. `ReceiveFrameAsync`, `ReceiveFramesAsync`, and `ReceiveMessageAsync` read from an in-memory channel fed by the pump. Each frame is delivered to exactly one consumer. Optional `SingleFrameConsumer` rejects a second concurrent `ReceiveFramesAsync` / `ReceiveFrameAsync`. If several tasks call `ReceiveMessageAsync` concurrently, logical message ordering is your responsibility. |
| **FrameReceived event** | Fires on the pump thread. Keep handlers short and non-blocking.                                                                                                                                                                                                                                                                                                                                      |


## Error Handling

All FrameWrench exceptions derive from `FrameWrenchException` and carry **actionable** detail: a stable `ErrorCode`, plain-language explanation, optional RFC section/URL, diagnostic `Context`, and `help:` suggestions in `Message`.

| Exception                        | When                                                                                                                                                         |
|----------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `WebSocketHandshakeException`    | HTTP upgrade failed, non-101 status, bad `Sec-WebSocket-Accept`, subprotocol mismatch                                                                      |
| `WebSocketClosedByPeerException` | Peer sent Close while you were on `ReceiveMessageAsync` (`CloseInfo` has status/reason; use `ReceiveFramesAsync` for the raw Close frame)                    |
| `WebSocketProtocolException`     | RFC 6455 violation — filter with `Kind` (`MaskedServerFrame`, `InvalidUtf8`, `Fragmentation`, `InvalidCloseStatus`, …)                                       |
| `WebSocketStateException`        | Operation invalid for current `State` (e.g. send while closed)                                                                                             |
| `EndOfStreamException`           | TCP closed mid-frame (not a `FrameWrenchException`)                                                                                                          |

Example message shape:

```text
error[FW-PROTO-MASKED-SERVER-FRAME]: received a masked frame from the server
...
Context:
  connectionState: Open
...
RFC 6455 §5.1 — https://datatracker.ietf.org/doc/html/rfc6455#section-5.1
help:
  → Confirm the URL is a WebSocket endpoint (ws/wss), not plain HTTP or raw TCP.
```

**Redaction:** error context never includes secret header values (`Authorization`, `Cookie`, `Sec-WebSocket-Key`); long strings are truncated.

```csharp
try
{
    await client.ConnectAsync(uri, ct);
}
catch (WebSocketHandshakeException ex)
{
    Console.Error.WriteLine($"[{ex.ErrorCode}] {ex.StatusLine}");
    Console.Error.WriteLine(ex.Message);
}
catch (WebSocketClosedByPeerException ex)
{
    Console.Error.WriteLine($"[{ex.ErrorCode}] code={ex.CloseInfo.StatusCode} reason={ex.CloseReason}");
}
catch (WebSocketProtocolException ex)
{
    Console.Error.WriteLine($"[{ex.ErrorCode}] {ex.Kind}");
    Console.Error.WriteLine(ex.Message);
    foreach (var (k, v) in ex.Detail.Context)
        Console.Error.WriteLine($"  {k}: {v}");
}
catch (FrameWrenchException ex)
{
    Console.Error.WriteLine($"[{ex.ErrorCode}]");
    Console.Error.WriteLine(ex.Message);
}
```

See [MIGRATION.md](MIGRATION.md) for the full 1.x → 2.0 API mapping.

Prefer **`await client.DisposeAsync()`** (or `await using`) over **`Dispose()`** when a `SynchronizationContext` may be present (for example, UI or legacy ASP.NET), because synchronous dispose can block while sending the close frame.


## Target Frameworks & Compatibility

| Target           | Runtime                                                       |
|------------------|---------------------------------------------------------------|
| `net462`         | .NET Framework 4.6.2 on Windows                               |
| `net48`          | .NET Framework 4.8 on Windows                                 |
| `netstandard2.0` | .NET Core 2.x, .NET Core 3.x, .NET 5, .NET 6, .NET 7, .NET 8+ |


## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch workflow, tests, XML documentation style, and CI/coverage setup.

## License

MIT - see `LICENSE` for details.


## Acknowledgements

- [RFC 6455 – The WebSocket Protocol](https://datatracker.ietf.org/doc/html/rfc6455)
