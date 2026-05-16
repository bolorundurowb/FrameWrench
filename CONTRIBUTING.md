# Contributing

Thank you for considering a contribution to FrameWrench.

1. Fork the repository and create a feature branch.
2. Ensure `dotnet test` passes on all targets before opening a PR.
3. Add tests for any new protocol behaviour.
4. Follow the existing XML-doc comment style.
5. **CI and Codecov:** pushes and PRs to `master` run `.github/workflows/ci.yml` (build, test, coverage). Maintainers should add a **`CODECOV_TOKEN`** repository secret from [Codecov](https://codecov.io) so coverage uploads succeed; forks may rely on Codecov's tokenless rules for public repositories when the upstream org allows it.

## Building

### Prerequisites

- **.NET SDK 10** or later (required for **C# 13**, which this repo pins in all projects)
- **.NET Framework 4.6.2** and **4.8** targeting packs / developer packs on Windows when building `net462`, `net48`, the sample, or `src/tests` for those monikers
- Visual Studio 2022 17.10+ or JetBrains Rider (for `.slnx` solution file support)

### Build

```bash
# Restore + build all targets
dotnet build src/FrameWrench.slnx

# Run unit and integration tests
dotnet test src/FrameWrench.slnx

# Run the example app (.NET Framework; pick one installed TFM)
dotnet run --project samples/FrameWrench.Example --framework net48
```

### Opening in Visual Studio

Open `src/FrameWrench.slnx` in Visual Studio 2022 17.10+. Older versions can open the individual `.csproj` files directly.


## Project Structure

```
FrameWrench/
├── README.md
├── CONTRIBUTING.md
│
├── src/
│   ├── FrameWrench.slnx                 # Solution file (VS 2022 17.10+)
│   ├── FrameWrench/
│   │   ├── FrameWrench.csproj
│   │   ├── FrameWrenchClient.cs         # Main client class
│   │   ├── FrameWrenchOptions.cs        # Configuration
│   │   ├── Core/
│   │   │   ├── FrameOpCode.cs           # RFC 6455 opcodes enum
│   │   │   ├── WebSocketFrame.cs        # Frame model + factory methods
│   │   │   ├── WebSocketCloseStatus.cs  # RFC 6455 close codes
│   │   │   ├── WebSocketState.cs        # Connection state machine
│   │   │   ├── WebSocketMessage.cs      # Reassembled message
│   │   │   └── FrameWrenchException.cs  # Exception hierarchy
│   │   └── Protocol/
│   │       ├── HandshakeHelper.cs       # HTTP Upgrade + SHA-1 accept
│   │       ├── FrameEncoder.cs          # Wire encoding + masking
│   │       └── FrameDecoder.cs          # Wire decoding + validation
│   └── tests/
│       └── FrameWrench.Tests/
│           ├── FrameWrench.Tests.csproj
│           ├── FrameEncoderTests.cs     # Encoder unit tests
│           ├── FrameDecoderTests.cs     # Decoder unit tests (all opcodes + lengths)
│           ├── HandshakeHelperTests.cs  # Handshake unit tests (RFC vector)
│           ├── WebSocketFrameTests.cs   # Frame model tests
│           ├── IntegrationTests.cs      # End-to-end tests vs in-process echo server
│           ├── Utf8ValidatorTests.cs    # UTF-8 / Close reason validation
│           └── IncomingUtf8MessageValidatorTests.cs
└── samples/
    └── FrameWrench.Example/
        ├── FrameWrench.Example.csproj
        └── Program.cs                   # Full demo: connect, text, ping, fragment, close
```