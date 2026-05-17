using System.Text;
using FrameWrench.Core;

namespace FrameWrench.Internal;

/// <summary>Formats <see cref="FrameWrenchErrorDetail"/> into multi-line exception messages.</summary>
internal static class FrameWrenchErrorFormatter
{
    public static string Format(FrameWrenchErrorDetail detail)
    {
        var sb = new StringBuilder();
        sb.Append("error[").Append(detail.Code).Append("]: ").AppendLine(detail.Title);

        if (!string.IsNullOrEmpty(detail.Explanation))
        {
            sb.AppendLine();
            sb.AppendLine(detail.Explanation.TrimEnd());
        }

        if (detail.Context.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Context:");
            foreach (var kv in detail.Context)
                sb.Append("  ").Append(kv.Key).Append(": ").AppendLine(kv.Value);
        }

        if (!string.IsNullOrEmpty(detail.Operation))
        {
            sb.AppendLine();
            sb.Append("Operation: ").AppendLine(detail.Operation);
        }

        if (!string.IsNullOrEmpty(detail.RfcSection))
        {
            sb.AppendLine();
            if (!string.IsNullOrEmpty(detail.RfcUrl))
                sb.Append(detail.RfcSection).Append(" — ").AppendLine(detail.RfcUrl);
            else
                sb.AppendLine(detail.RfcSection);
        }

        if (detail.Suggestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("help:");
            foreach (var s in detail.Suggestions)
                sb.Append("  → ").AppendLine(s);
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatWithInner(FrameWrenchException ex)
    {
        var sb = new StringBuilder(Format(ex.Detail));
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            sb.AppendLine();
            sb.Append("Caused by: ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
        }

        return sb.ToString();
    }
}
