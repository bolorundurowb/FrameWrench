using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>
/// Validates UTF-8 across fragmented Text frames without decoding to string until complete.
/// </summary>
internal sealed class Utf8IncrementalValidator
{
    private List<byte>? _buffer;

    public void Reset() => _buffer = null;

    public void ValidateFragment(ReadOnlySpan<byte> utf8Bytes, bool isFinal, bool inbound)
    {
        if (_buffer is null && isFinal)
        {
            Utf8Validator.ThrowIfInvalidUtf8(utf8Bytes, inbound);
            return;
        }

        if (_buffer is null)
        {
            if (utf8Bytes.IsEmpty)
                return;

            _buffer = new List<byte>(utf8Bytes.Length);
        }

        if (!utf8Bytes.IsEmpty)
            _buffer.AddRange(utf8Bytes.ToArray());

        if (isFinal)
        {
            Utf8Validator.ThrowIfInvalidUtf8(_buffer.ToArray(), inbound);
            _buffer = null;
        }
    }
}
