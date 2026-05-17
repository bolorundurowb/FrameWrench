using FrameWrench.Internal;
using Shouldly;
using Xunit;

namespace FrameWrench.Tests;

public class PrependStreamTests
{
    [Fact]
    public void Constructor_NullInnerStream_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new PrependStream(ReadOnlyMemory<byte>.Empty, null!));
    }

    [Fact]
    public void CanRead_AlwaysTrue()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        stream.CanRead.ShouldBeTrue();
    }

    [Fact]
    public void CanWrite_DelegatesToInner()
    {
        var inner = new MemoryStream();
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);
        stream.CanWrite.ShouldBe(inner.CanWrite);
    }

    [Fact]
    public void CanSeek_AlwaysFalse()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        stream.CanSeek.ShouldBeFalse();
    }

    [Fact]
    public void Length_ThrowsNotSupportedException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<NotSupportedException>(() => _ = stream.Length);
    }

    [Fact]
    public void Position_Get_ThrowsNotSupportedException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<NotSupportedException>(() => _ = stream.Position);
    }

    [Fact]
    public void Position_Set_ThrowsNotSupportedException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<NotSupportedException>(() => stream.Position = 0);
    }

    [Fact]
    public void Seek_ThrowsNotSupportedException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength_ThrowsNotSupportedException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<NotSupportedException>(() => stream.SetLength(0));
    }

    [Fact]
    public void Read_NullBuffer_ThrowsArgumentNullException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<ArgumentNullException>(() => stream.Read(null!, 0, 0));
    }

    [Fact]
    public void Read_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<ArgumentOutOfRangeException>(() => stream.Read(new byte[4], -1, 0));
    }

    [Fact]
    public void Read_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<ArgumentOutOfRangeException>(() => stream.Read(new byte[4], 0, -1));
    }

    [Fact]
    public void Read_InvalidBufferRange_ThrowsArgumentException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.Throw<ArgumentException>(() => stream.Read(new byte[4], 2, 4));
    }

    [Fact]
    public async Task ReadAsync_NullBuffer_ThrowsArgumentNullException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        await Should.ThrowAsync<ArgumentNullException>(
            () => stream.ReadAsync(null!, 0, 0, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => stream.ReadAsync(new byte[4], -1, 0, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_InvalidBufferRange_ThrowsArgumentException()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        await Should.ThrowAsync<ArgumentException>(
            () => stream.ReadAsync(new byte[4], 2, 4, CancellationToken.None));
    }

    [Fact]
    public void Read_EmptyPrefix_ReadsDirectlyFromInner()
    {
        var innerData = new byte[] { 1, 2, 3, 4 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);

        var buf = new byte[4];
        var n = stream.Read(buf, 0, 4);

        n.ShouldBe(4);
        buf.ShouldBe(innerData);
    }

    [Fact]
    public void Read_PrefixOnly_ReturnsPrefixBytes()
    {
        var prefix = new byte[] { 10, 20, 30 };
        using var inner = new MemoryStream();
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), inner);

        var buf = new byte[3];
        var n = stream.Read(buf, 0, 3);

        n.ShouldBe(3);
        buf.ShouldBe(prefix);
    }

    [Fact]
    public void Read_PartialPrefixRead_ThenRemainingPrefix()
    {
        var prefix = new byte[] { 1, 2, 3, 4 };
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), new MemoryStream());

        var buf = new byte[4];
        var n1 = stream.Read(buf, 0, 2);
        var n2 = stream.Read(buf, 2, 2);

        n1.ShouldBe(2);
        n2.ShouldBe(2);
        buf.ShouldBe(prefix);
    }

    [Fact]
    public void Read_LargeEnough_ReadsPrefixAndInnerInSingleCall()
    {
        var prefix = new byte[] { 1, 2 };
        var innerData = new byte[] { 3, 4 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), inner);

        var buf = new byte[4];
        var n = stream.Read(buf, 0, 4);

        n.ShouldBe(4);
        buf.ShouldBe(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void Read_PrefixExhausted_ThenSubsequentReadGoesToInner()
    {
        var prefix = new byte[] { 1, 2 };
        var innerData = new byte[] { 3, 4, 5 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), inner);

        var buf = new byte[5];
        var n1 = stream.Read(buf, 0, 2);
        var n2 = stream.Read(buf, 2, 3);

        n1.ShouldBe(2);
        n2.ShouldBe(3);
        buf.ShouldBe(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void Read_WithNonZeroOffset_WritesPrefixAtCorrectPosition()
    {
        var prefix = new byte[] { 7, 8, 9 };
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), new MemoryStream());

        var buf = new byte[6];
        var n = stream.Read(buf, 3, 3);

        n.ShouldBe(3);
        buf[3].ShouldBe((byte)7);
        buf[4].ShouldBe((byte)8);
        buf[5].ShouldBe((byte)9);
    }

    [Fact]
    public async Task ReadAsync_EmptyPrefix_ReadsDirectlyFromInner()
    {
        var innerData = new byte[] { 5, 6, 7 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);

        var buf = new byte[3];
        var n = await stream.ReadAsync(buf, 0, 3, CancellationToken.None);

        n.ShouldBe(3);
        buf.ShouldBe(innerData);
    }

    [Fact]
    public async Task ReadAsync_PrefixAndInner_MergesInSingleCall()
    {
        var prefix = new byte[] { 10, 20 };
        var innerData = new byte[] { 30, 40 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), inner);

        var buf = new byte[4];
        var n = await stream.ReadAsync(buf, 0, 4, CancellationToken.None);

        n.ShouldBe(4);
        buf.ShouldBe(new byte[] { 10, 20, 30, 40 });
    }

    [Fact]
    public async Task ReadAsync_PrefixExhaustedByFirstCall_SecondCallReadsFromInner()
    {
        var prefix = new byte[] { 1, 2 };
        var innerData = new byte[] { 3, 4 };
        using var inner = new MemoryStream(innerData);
        using var stream = new PrependStream(new ReadOnlyMemory<byte>(prefix), inner);

        var buf = new byte[4];
        var n1 = await stream.ReadAsync(buf, 0, 2, CancellationToken.None);
        var n2 = await stream.ReadAsync(buf, 2, 2, CancellationToken.None);

        n1.ShouldBe(2);
        n2.ShouldBe(2);
        buf.ShouldBe(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void Write_DelegatesToInner()
    {
        using var inner = new MemoryStream();
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);

        var data = new byte[] { 1, 2, 3 };
        stream.Write(data, 0, 3);

        inner.ToArray().ShouldBe(data);
    }

    [Fact]
    public async Task WriteAsync_DelegatesToInner()
    {
        using var inner = new MemoryStream();
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);

        var data = new byte[] { 4, 5, 6 };
        await stream.WriteAsync(data, 0, 3, CancellationToken.None);

        inner.ToArray().ShouldBe(data);
    }

    [Fact]
    public void Flush_DelegatesToInnerWithoutThrowing()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        Should.NotThrow(() => stream.Flush());
    }

    [Fact]
    public async Task FlushAsync_DelegatesToInnerWithoutThrowing()
    {
        using var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, new MemoryStream());
        await Should.NotThrowAsync(() => stream.FlushAsync(CancellationToken.None));
    }

    [Fact]
    public void Dispose_DisposesInnerStream()
    {
        var inner = new MemoryStream();
        var stream = new PrependStream(ReadOnlyMemory<byte>.Empty, inner);

        stream.Dispose();

        inner.CanRead.ShouldBeFalse();
    }
}
