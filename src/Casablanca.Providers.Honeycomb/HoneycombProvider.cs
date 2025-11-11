using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;

namespace Casablanca.Providers.Honeycomb;

public sealed class HoneycombProvider : IObservabilityProvider
{
    private readonly HttpClient _client;
    private readonly string _apiUrl;
    private readonly string _dataset;

    public string Name { get; }

    public HoneycombProvider(string name, string apiUrl, string apiKey, string dataset)
    {
        Name = name;
        _apiUrl = apiUrl.TrimEnd('/');
        _dataset = dataset;

        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add("X-Honeycomb-Team", apiKey);
    }

    public async Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var filters = BuildFilters(query);

        var body = new
        {
            start_time = from.ToString("o"),
            end_time = to.ToString("o"),
            filters,
            filter_combination = "AND",
            limit = 100,
            order = new[] { "-time" }
        };

        using var response = await _client.PostAsJsonAsync(
            $"{_apiUrl}/1/queries/{Uri.EscapeDataString(_dataset)}/run",
            body,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var list = new List<TraceSpan>();

        if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in resultsEl.EnumerateArray())
        {
            var data = item.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
                ? dataEl
                : item;

            var attributes = new Dictionary<string, object?>();
            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in data.EnumerateObject())
                {
                    attributes[prop.Name] = ExtractJsonValue(prop.Value);
                }
            }

            var id = GetString(data, "trace.span_id") ?? GetString(data, "span_id") ?? GetString(data, "id") ?? string.Empty;
            var traceId = GetString(data, "trace.trace_id") ?? GetString(data, "trace_id") ?? string.Empty;
            var name = GetString(data, "name") ?? GetString(data, "span.name") ?? string.Empty;
            var service = GetString(data, "service.name") ?? GetString(data, "service_name");

            var duration = GetDouble(data, "duration_ms") ?? GetDouble(data, "duration_ms") ?? GetDouble(data, "duration");
            var durationTs = duration.HasValue ? TimeSpan.FromMilliseconds(duration.Value) : TimeSpan.Zero;

            var time = GetString(data, "time") ?? GetString(data, "timestamp");
            var ts = TryParseDateTimeOffset(time) ?? DateTimeOffset.MinValue;

            list.Add(new TraceSpan
            {
                Id = id,
                TraceId = traceId,
                Name = name,
                Service = service,
                Duration = durationTs,
                StartTime = ts,
                Attributes = attributes
            });
        }

        return list;
    }

    public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());
    }

    private static object? ExtractJsonValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(ExtractJsonValue).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ExtractJsonValue(p.Value)),
            _ => null
        };

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static double? GetDouble(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : null;

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, out var dto) ? dto : null;

    private static IReadOnlyList<object> BuildFilters(string query)
    {
        var pairs = ParseSimpleQuery(query);
        var filters = new List<object>();
        foreach (var (key, value) in pairs)
        {
            var column = MapToHoneycombColumn(key);
            var op = column == "name" ? "contains" : "=";
            filters.Add(new { column, op, value });
        }
        return filters;
    }

    private static string MapToHoneycombColumn(string key)
    {
        var k = key.Trim();
        return k.ToLowerInvariant() switch
        {
            "service" => "service.name",
            "service.name" => "service.name",
            "operation_name" => "name",
            "operation.name" => "name",
            "name" => "name",
            _ => k.Contains('.') ? k : $"attributes.{k}"
        };
    }

    private static List<(string key, string value)> ParseSimpleQuery(string query)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(query)) return list;
        var parts = Regex.Matches(query, "[^\\\"]+|\\\"[^\\\"]*\\\"")
                         .Select(m => m.Value)
                         .ToList();
        string? currentKey = null;
        var currentValue = new List<string>();
        foreach (var part in parts)
        {
            if (part.Contains(':'))
            {
                if (currentKey != null)
                {
                    list.Add((currentKey, string.Join(' ', currentValue).Trim('\"')));
                    currentValue.Clear();
                }
                var idx = part.IndexOf(':');
                currentKey = part[..idx];
                var value = part[(idx + 1)..];
                if (!string.IsNullOrEmpty(value))
                    currentValue.Add(value);
            }
            else if (currentKey != null)
            {
                currentValue.Add(part);
            }
        }
        if (currentKey != null)
        {
            list.Add((currentKey, string.Join(' ', currentValue).Trim('\"')));
        }
        return list;
    }
}

