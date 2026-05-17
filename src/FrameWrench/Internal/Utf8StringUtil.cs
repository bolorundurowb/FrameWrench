using System.Runtime.InteropServices;
using System.Text;

namespace FrameWrench.Internal;

/// <summary>
/// Zero-allocation UTF-8 decoding from <see cref="ReadOnlyMemory{T}"/>: when the memory
/// is backed by a managed array, the array segment is passed directly to
/// <see cref="Encoding.UTF8"/> to avoid the extra heap copy that <c>.ToArray()</c> would incur.
/// </summary>
internal static class Utf8StringUtil
{
    public static string GetString(ReadOnlyMemory<byte> bytes)
    {
        if (MemoryMarshal.TryGetArray(bytes, out var seg) && seg.Array is not null)
            return Encoding.UTF8.GetString(seg.Array, seg.Offset, seg.Count);

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
