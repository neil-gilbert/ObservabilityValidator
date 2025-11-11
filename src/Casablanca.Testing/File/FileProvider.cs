using System.Text.Json;
using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;

namespace Casablanca.Testing.File;

public sealed class FileProvider : IObservabilityProvider
{
    private readonly string _path;
    public string Name { get; }

    public FileProvider(string path, string name = "file")
    {
        _path = path;
        Name = name;
    }

    public async Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var spans = new List<TraceSpan>();
        await foreach (var line in ReadLinesAsync(_path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                spans.Add(ParseSpan(doc.RootElement));
            }
            catch
            {
                // skip malformed line
            }
        }

        return spans
            .Where(s => s.StartTime >= from && s.StartTime <= to)
            .Where(InMemoryPredicate(query))
            .ToList();
    }

    public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());

    private static async IAsyncEnumerable<string> ReadLinesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        while (!sr.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await sr.ReadLineAsync();
            if (line is not null)
                yield return line;
        }
    }

    private static TraceSpan ParseSpan(JsonElement el)
    {
        var span = new TraceSpan();

        if (el.TryGetProperty("id", out var id)) span.Id = id.GetString() ?? string.Empty;
        if (el.TryGetProperty("traceId", out var tid)) span.TraceId = tid.GetString() ?? string.Empty;
        if (el.TryGetProperty("name", out var name)) span.Name = name.GetString() ?? string.Empty;
        if (el.TryGetProperty("service", out var svc)) span.Service = svc.GetString();
        if (el.TryGetProperty("startTime", out var st) && st.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(st.GetString(), out var dto))
            span.StartTime = dto;
        if (el.TryGetProperty("durationMs", out var dms) && dms.ValueKind == JsonValueKind.Number)
            span.Duration = TimeSpan.FromMilliseconds(dms.GetDouble());

        var attrs = new Dictionary<string, object?>();
        if (el.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in a.EnumerateObject())
                attrs[p.Name] = Extract(p.Value);
        }
        span.Attributes = attrs;

        return span;
    }

    private static object? Extract(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => v.EnumerateArray().Select(Extract).ToArray(),
        JsonValueKind.Object => v.EnumerateObject().ToDictionary(p => p.Name, p => Extract(p.Value)),
        _ => null
    };

    private static Func<TraceSpan, bool> InMemoryPredicate(string query)
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

