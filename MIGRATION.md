# Migrating to FrameWrench 2.0

FrameWrench 2.0 is a breaking release focused on **actionable errors**, **stricter RFC 6455 validation**, and a clearer public API.

## Options (immutable)

**Before (1.x):**

```csharp
var options = new FrameWrenchOptions
{
    ConnectTimeout = TimeSpan.FromSeconds(15),
    ExtraHeaders = { ["Authorization"] = "Bearer token" },
};
```

**After (2.0):**

```csharp
var options = FrameWrenchOptions.Create()
    .WithConnectTimeout(TimeSpan.FromSeconds(15))
    .WithExtraHeader("Authorization", "Bearer token")
    .Build();
```

Use `FrameWrenchOptions.Default` when you need library defaults.

## Connect / Ping / Close results

| 1.x | 2.0 |
|-----|-----|
| `await client.ConnectAsync(uri);` | `var connect = await client.ConnectAsync(uri);` → `connect.SelectedSubProtocol` |
| `(bool ok, TimeSpan rt) = await client.PingAsync(...)` | `PingResult r = await client.PingAsync(...)` → `r.PongReceived`, `r.Elapsed` |
| `await client.CloseAsync(...);` then inspect `State` | `CloseResult r = await client.CloseAsync(...)` → `r.HandshakeCompleted`, `r.FinalState` |

## Close status codes

Use `WireCloseStatus` for frames you send (`CloseAsync`, `WebSocketFrame.Close`).

`WebSocketCloseStatus` remains for **local-only** pseudo-codes (`NoStatusReceived`, `AbnormalClosure`, etc.) — never put those on the wire.

**Before:** `WebSocketFrame.Close(WebSocketCloseStatus.NormalClosure, "bye")`  
**After:** `WebSocketFrame.Close(WireCloseStatus.NormalClosure, "bye")`

**Before:** `frame.GetCloseData(out var status, out var reason)`  
**After:** `CloseFrameInfo info = frame.GetCloseInfo()`

## Receive API

Prefer `ReceiveFramesAsync` over `GetFrameStream` (obsolete).

```csharp
await foreach (var frame in client.ReceiveFramesAsync(ct))
{
    // ...
}
```

## Errors

All library exceptions expose:

- `ex.ErrorCode` — stable id (e.g. `FW-PROTO-MASKED-SERVER-FRAME`)
- `ex.Detail` — structured context, RFC links, and `help:` suggestions
- `WebSocketProtocolException.Kind` — `ProtocolViolationKind` for filtering

Example:

```csharp
catch (WebSocketProtocolException ex)
{
    Console.WriteLine(ex.ErrorCode);
    Console.WriteLine(ex.Message); // multi-line, human-readable
}
```

## Behavioral changes

- Invalid inbound Close status codes abort the connection (RFC §7.4.1).
- Decoder protocol errors complete the frame channel as `WebSocketProtocolException`.
- `ExtraHeaders` cannot override reserved WebSocket handshake headers.
- Header names/values must not contain CR, LF, or NUL.
