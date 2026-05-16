namespace FrameWrench.Internal;

/// <summary>
/// A read-through stream that serves a fixed prefix before delegating to an inner stream.
/// Used to preserve bytes read past the HTTP handshake header block.
/// </summary>
internal sealed class PrependStream : Stream
{
    private readonly byte[] _prefix;
    private int _prefixOffset;
    private readonly Stream _inner;

    public PrependStream(ReadOnlyMemory<byte> prefix, Stream inner)
    {
        if (inner is null) throw new ArgumentNullException(nameof(inner));

        _prefix = prefix.IsEmpty ? [] : prefix.ToArray();
        _inner = inner;
    }

    public override bool CanRead => true;

    public override bool CanWrite => _inner.CanWrite;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBuffer(buffer, offset, count);

        var fromPrefix = ReadFromPrefix(buffer, offset, count);
        if (fromPrefix == count || _prefixOffset >= _prefix.Length)
            return fromPrefix;

        return fromPrefix + _inner.Read(buffer, offset + fromPrefix, count - fromPrefix);
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBuffer(buffer, offset, count);

        var fromPrefix = ReadFromPrefix(buffer, offset, count);
        if (fromPrefix == count || _prefixOffset >= _prefix.Length)
            return fromPrefix;

        return fromPrefix + await _inner
            .ReadAsync(buffer, offset + fromPrefix, count - fromPrefix, cancellationToken)
            .ConfigureAwait(false);
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        _inner.Write(buffer, offset, count);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    private int ReadFromPrefix(byte[] buffer, int offset, int count)
    {
        if (_prefixOffset >= _prefix.Length)
            return 0;

        var available = _prefix.Length - _prefixOffset;
        var toCopy = Math.Min(count, available);
        Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, toCopy);
        _prefixOffset += toCopy;
        return toCopy;
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (buffer.Length - offset < count)
            throw new ArgumentException("Invalid buffer range.");
    }
}
