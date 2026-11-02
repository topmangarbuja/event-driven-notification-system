using System.Diagnostics;
using System.Text;

namespace src;

public static class MessageTracing
{
    public const string ActivitySourceName = "EventDrivenNotificationSystem.Messaging";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static IDictionary<string, object?> CreateHeaders(Activity? activity)
    {
        var headers = new Dictionary<string, object?>();
        activity ??= Activity.Current;

        if (activity?.Id is not { } traceParent)
        {
            return headers;
        }

        headers["traceparent"] = Encoding.UTF8.GetBytes(traceParent);
        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
        {
            headers["tracestate"] = Encoding.UTF8.GetBytes(activity.TraceStateString);
        }

        return headers;
    }

    public static ActivityContext ExtractContext(IDictionary<string, object?>? headers)
    {
        return TryExtractContext(headers, out var context) ? context : default;
    }

    public static bool TryExtractContext(IDictionary<string, object?>? headers, out ActivityContext context)
    {
        context = default;

        if (headers is null ||
            !TryReadHeader(headers, "traceparent", out var traceParent) ||
            string.IsNullOrWhiteSpace(traceParent))
        {
            return false;
        }

        var traceState = TryReadHeader(headers, "tracestate", out var state)
            ? state
            : null;

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out context);
    }

    private static bool TryReadHeader(IDictionary<string, object?> headers, string name, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(name, out var header))
        {
            return false;
        }

        switch (header)
        {
            case byte[] bytes:
                value = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrWhiteSpace(value);
            case string text:
                value = text;
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }
}
