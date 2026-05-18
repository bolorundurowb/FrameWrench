namespace FrameWrench.Internal;

/// <summary>Builds context dictionaries for <see cref="Core.FrameWrenchErrorDetail"/>.</summary>
internal static class ErrorContext
{
    public static Dictionary<string, string> Create(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            d[key] = value;
        return d;
    }

    public static Dictionary<string, string> CreateHeaderContext(string headerName) =>
        Create(("headerName", headerName));

    public static string FormatOpcode(Core.FrameOpCode op) =>
        $"{op} (0x{(byte)op:X1})";

    public static string FormatState(Core.WebSocketState state) => state.ToString();

    public static string Truncate(string? value, int max = 80)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value!.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
