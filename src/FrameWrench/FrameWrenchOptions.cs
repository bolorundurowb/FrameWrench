using System.Net.Security;
using System.Security.Authentication;
using FrameWrench.Internal;

namespace FrameWrench;

/// <summary>
/// Immutable configuration for a <see cref="FrameWrenchClient"/> instance.
/// </summary>
/// <remarks>
/// Create instances via <see cref="Create"/> or use <see cref="Default"/>.
/// Headers and subprotocols are snapshotted and validated at build time.
/// </remarks>
public sealed class FrameWrenchOptions
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default options (30s connect, 64 MiB limits, validation enabled).</summary>
    public static FrameWrenchOptions Default { get; } = Create().Build();

    /// <summary>Starts building a new options instance.</summary>
    public static Builder Create() => new();

    private FrameWrenchOptions(Builder builder)
    {
        ConnectTimeout = builder.ConnectTimeout;
        SslProtocols = builder.SslProtocols;
        RemoteCertificateValidationCallback = builder.RemoteCertificateValidationCallback;
        MaxFramePayloadBytes = builder.MaxFramePayloadBytes;
        MaxMessagePayloadBytes = builder.MaxMessagePayloadBytes;
        AutoPing = builder.AutoPing;
        KeepAliveInterval = builder.KeepAliveInterval;
        PingTimeout = builder.PingTimeout;
        CloseHandshakeTimeout = builder.CloseHandshakeTimeout;
        ReceiveChannelCapacity = builder.ReceiveChannelCapacity;
        ValidateOutgoingMessages = builder.ValidateOutgoingMessages;
        FailOnInvalidIncomingUtf8 = builder.FailOnInvalidIncomingUtf8;
        SingleFrameConsumer = builder.SingleFrameConsumer;

        ExtraHeaders = builder.ExtraHeaders.Count == 0
            ? EmptyHeaders
            : new Dictionary<string, string>(builder.ExtraHeaders, StringComparer.OrdinalIgnoreCase);

        SubProtocols = builder.SubProtocols.Count == 0
            ? Array.Empty<string>()
            : builder.SubProtocols.ToArray();

        HttpHeaderValidator.ValidateExtraHeaders(ExtraHeaders);
    }

    public TimeSpan ConnectTimeout { get; }
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; }
    public IReadOnlyList<string> SubProtocols { get; }
    public SslProtocols SslProtocols { get; }
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; }
    public int MaxFramePayloadBytes { get; }
    public int MaxMessagePayloadBytes { get; }
    public bool AutoPing { get; }
    public TimeSpan KeepAliveInterval { get; }
    public TimeSpan PingTimeout { get; }
    public TimeSpan CloseHandshakeTimeout { get; }
    public int? ReceiveChannelCapacity { get; }
    public bool ValidateOutgoingMessages { get; }
    public bool FailOnInvalidIncomingUtf8 { get; }
    public bool SingleFrameConsumer { get; }

    /// <summary>Mutable builder for <see cref="FrameWrenchOptions"/>.</summary>
    public sealed class Builder
    {
        internal Dictionary<string, string> ExtraHeaders { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal List<string> SubProtocols { get; } = [];

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public SslProtocols SslProtocols { get; set; } = SslProtocols.None;
        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }
        public int MaxFramePayloadBytes { get; set; } = 64 * 1024 * 1024;
        public int MaxMessagePayloadBytes { get; set; } = 64 * 1024 * 1024;
        public bool AutoPing { get; set; }
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan CloseHandshakeTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public int? ReceiveChannelCapacity { get; set; }
        public bool ValidateOutgoingMessages { get; set; } = true;
        public bool FailOnInvalidIncomingUtf8 { get; set; } = true;
        public bool SingleFrameConsumer { get; set; }

        public Builder WithConnectTimeout(TimeSpan value)
        {
            ConnectTimeout = value;
            return this;
        }

        public Builder WithExtraHeader(string name, string value)
        {
            ExtraHeaders[name] = value;
            return this;
        }

        public Builder WithExtraHeaders(IEnumerable<KeyValuePair<string, string>> headers)
        {
            foreach (var kv in headers)
                ExtraHeaders[kv.Key] = kv.Value;
            return this;
        }

        public Builder WithSubProtocol(string protocol)
        {
            SubProtocols.Add(protocol);
            return this;
        }

        public Builder WithSubProtocols(IEnumerable<string> protocols)
        {
            SubProtocols.AddRange(protocols);
            return this;
        }

        public Builder WithSslProtocols(SslProtocols protocols)
        {
            SslProtocols = protocols;
            return this;
        }

        public Builder WithRemoteCertificateValidationCallback(
            RemoteCertificateValidationCallback? callback)
        {
            RemoteCertificateValidationCallback = callback;
            return this;
        }

        public Builder WithMaxFramePayloadBytes(int bytes)
        {
            MaxFramePayloadBytes = bytes;
            return this;
        }

        public Builder WithMaxMessagePayloadBytes(int bytes)
        {
            MaxMessagePayloadBytes = bytes;
            return this;
        }

        public Builder WithAutoPing(bool enabled = true)
        {
            AutoPing = enabled;
            return this;
        }

        public Builder WithKeepAliveInterval(TimeSpan interval)
        {
            KeepAliveInterval = interval;
            return this;
        }

        public Builder WithPingTimeout(TimeSpan timeout)
        {
            PingTimeout = timeout;
            return this;
        }

        public Builder WithCloseHandshakeTimeout(TimeSpan timeout)
        {
            CloseHandshakeTimeout = timeout;
            return this;
        }

        public Builder WithReceiveChannelCapacity(int? capacity)
        {
            ReceiveChannelCapacity = capacity;
            return this;
        }

        public Builder WithValidateOutgoingMessages(bool validate)
        {
            ValidateOutgoingMessages = validate;
            return this;
        }

        public Builder WithFailOnInvalidIncomingUtf8(bool fail)
        {
            FailOnInvalidIncomingUtf8 = fail;
            return this;
        }

        public Builder WithSingleFrameConsumer(bool single)
        {
            SingleFrameConsumer = single;
            return this;
        }

        public FrameWrenchOptions Build() => new(this);
    }
}
