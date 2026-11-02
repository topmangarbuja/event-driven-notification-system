using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using src;

namespace tests;

public class MessageTracingTest
{
    [Fact]
    public void CreateHeaders_WhenActivityExists_IncludesTraceParentAndTraceState()
    {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity.TraceStateString = "rojo=00f067aa0ba902b7";

        var headers = MessageTracing.CreateHeaders(activity);

        headers.Should().ContainKey("traceparent");
        headers.Should().ContainKey("tracestate");

        var traceParent = Encoding.UTF8.GetString((byte[])headers["traceparent"]!);
        traceParent.Should().StartWith("00-");

        var traceState = Encoding.UTF8.GetString((byte[])headers["tracestate"]!);
        traceState.Should().Be("rojo=00f067aa0ba902b7");
    }

    [Fact]
    public void ExtractContext_WhenValidTraceHeadersProvided_ParsesRemoteContext()
    {
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
            ["tracestate"] = Encoding.UTF8.GetBytes("rojo=00f067aa0ba902b7")
        };

        var context = MessageTracing.ExtractContext(headers);

        context.TraceId.ToHexString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        context.SpanId.ToHexString().Should().Be("00f067aa0ba902b7");
    }

    [Fact]
    public void TryExtractContext_WhenTraceParentMissing_ReturnsFalse()
    {
        var headers = new Dictionary<string, object?>
        {
            ["tracestate"] = Encoding.UTF8.GetBytes("rojo=00f067aa0ba902b7")
        };

        var result = MessageTracing.TryExtractContext(headers, out var context);

        result.Should().BeFalse();
        context.Should().Be(default(ActivityContext));
    }

    [Fact]
    public void TryExtractContext_WhenTraceParentMalformed_ReturnsFalse()
    {
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes("not-a-valid-traceparent")
        };

        var result = MessageTracing.TryExtractContext(headers, out var context);

        result.Should().BeFalse();
        context.Should().Be(default(ActivityContext));
    }
}
