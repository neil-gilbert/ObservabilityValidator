using System.Diagnostics;
using Casablanca.Abstractions.Model;

namespace Casablanca.Testing.Tracing;

public sealed class TraceRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<TraceSpan> _spans = new();

    public IReadOnlyList<TraceSpan> Spans => _spans;

    public TraceRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private void OnActivityStarted(Activity activity)
    {
    }

    private void OnActivityStopped(Activity activity)
    {
        var span = new TraceSpan
        {
            Id = activity.SpanId.ToHexString(),
            TraceId = activity.TraceId.ToHexString(),
            Name = activity.DisplayName,
            Service = GetTag(activity, "service.name"),
            Duration = activity.Duration,
            StartTime = activity.StartTimeUtc == default ? DateTimeOffset.UtcNow : new DateTimeOffset(activity.StartTimeUtc),
            Attributes = CollectAttributes(activity)
        };

        span.Attributes["trace.span_id"] = span.Id;
        span.Attributes["trace.trace_id"] = span.TraceId;
        if (span.Service is not null)
            span.Attributes["service.name"] = span.Service;

        _spans.Add(span);
    }

    private static string? GetTag(Activity activity, string key)
        => activity.Tags.FirstOrDefault(t => t.Key == key).Value
           ?? activity.TagObjects.FirstOrDefault(t => t.Key == key).Value?.ToString();

    private static Dictionary<string, object?> CollectAttributes(Activity activity)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in activity.TagObjects)
        {
            dict[tag.Key] = tag.Value;
        }
        foreach (var b in activity.Baggage)
        {
            dict[$"baggage.{b.Key}"] = b.Value;
        }
        return dict;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}

