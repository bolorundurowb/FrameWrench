using System.Runtime.InteropServices;
using System.Text;

namespace FrameWrench.Internal;

/// <summary>
/// UTF-8 decoding from <see cref="ReadOnlyMemory{T}"/>, using the underlying array when available.
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
