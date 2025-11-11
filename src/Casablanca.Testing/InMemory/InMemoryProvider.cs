using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;

namespace Casablanca.Testing.InMemory;

public sealed class InMemoryProvider : IObservabilityProvider
{
    private readonly IReadOnlyList<TraceSpan> _spans;
    public string Name { get; }

    public InMemoryProvider(IEnumerable<TraceSpan> spans, string name = "in-memory")
    {
        _spans = spans.ToList();
        Name = name;
    }

    public Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var filtered = _spans
            .Where(s => s.StartTime >= from && s.StartTime <= to)
            .Where(BuildPredicate(query))
            .ToList();

        return Task.FromResult<IReadOnlyList<TraceSpan>>(filtered);
    }

    public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());

    private static Func<TraceSpan, bool> BuildPredicate(string query)
    {
        var pairs = ParseSimpleQuery(query);
        return span => pairs.All(kv => Matches(span, kv.key, kv.value));
    }

    private static bool Matches(TraceSpan span, string key, string value)
    {
        var k = key.ToLowerInvariant();
        if (k is "service" or "service.name")
            return string.Equals(span.Service, value, StringComparison.OrdinalIgnoreCase);
        if (k is "operation_name" or "name")
            return span.Name?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

        if (span.Attributes.TryGetValue(key, out var v) || span.Attributes.TryGetValue($"attributes.{key}", out v))
            return string.Equals(Normalise(v), value, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static string? Normalise(object? v)
        => v switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => v.ToString()
        };

    private static List<(string key, string value)> ParseSimpleQuery(string query)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(query)) return list;

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = token.IndexOf(':');
            if (idx <= 0 || idx == token.Length - 1) continue;
            var key = token[..idx];
            var value = token[(idx + 1)..];
            list.Add((key, value));
        }

        return list;
    }
}

