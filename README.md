<div align="center">
  <img
    src="https://raw.githubusercontent.com/bolorundurowb/FrameWrench/refs/heads/master/assets/frame-wrench-logo.png"
    alt="FrameWrench logo"  />
</div>

# FrameWrench

[![Build, Test & Coverage](https://github.com/bolorundurowb/FrameWrench/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/bolorundurowb/FrameWrench/actions/workflows/build-and-test.yml) [![codecov](https://codecov.io/gh/bolorundurowb/FrameWrench/graph/badge.svg?token=poFOTCdIj8)](https://codecov.io/gh/bolorundurowb/FrameWrench) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) ![NuGet Version](https://img.shields.io/nuget/v/FrameWrench)

**A lightweight, client-only RFC 6455 WebSocket library for .NET Framework 4.6.2+, .NET Framework 4.8, and .NET Standard 2.0.**

FrameWrench gives you explicit, frame-level control over WebSocket connections — Ping/Pong correlation, fragmentation, `ReceiveFramesAsync`, and actionable protocol errors — with a message-level API when you do not need per-frame handling.

> ⚠️ **AI Disclosure:** This project was developed with the assistance of generative AI. All code, architecture decisions, and documentation were reviewed and refined as part of the development process.

> **Upgrading from 1.x?** See [MIGRATION.md](MIGRATION.md) for breaking changes in 2.0.

## Install

```bash
dotnet add package FrameWrench
```

Targets: `net462`, `net48`, `netstandard2.0`. See the [documentation](https://bolorundurowb.github.io/FrameWrench#installation) for pinned versions and project-reference setup.

## Quick start

Use one `FrameWrenchClient` per connection; create a new instance after close.

```csharp
using FrameWrench;
using FrameWrench.Core;

await using var client = new FrameWrenchClient();
await client.ConnectAsync(new Uri("wss://echo.websocket.org"));

await client.SendTextAsync("Hello, World!");
var message = await client.ReceiveMessageAsync();
Console.WriteLine(message.GetText());

await client.CloseAsync(WireCloseStatus.NormalClosure, "bye");
```

## Detailed Documentation

For details on options, Ping/Pong, fragmentation, TLS, and error handling, see the [full documentation](https://bolorundurowb.github.io/FrameWrench).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT — see [LICENSE](LICENSE).
