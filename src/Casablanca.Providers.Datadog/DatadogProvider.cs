using System.Net.Http.Json;
using System.Text.Json;
using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;

namespace Casablanca.Providers.Datadog;

public sealed class DatadogProvider : IObservabilityProvider
{
    private readonly HttpClient _client;
    public string Name { get; }

    private readonly string _apiUrl;

    public DatadogProvider(string name, string apiUrl, string apiKey, string appKey)
    {
        Name = name;
        _apiUrl = apiUrl.TrimEnd('/');

        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add("DD-API-KEY", apiKey);
        _client.DefaultRequestHeaders.Add("DD-APPLICATION-KEY", appKey);
    }

    public async Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            filter = new
            {
                query,
                from = from.ToString("o"),
                to = to.ToString("o")
            },
            page = new { limit = 100 }
        };

        using var response = await _client.PostAsJsonAsync(
            $"{_apiUrl}/api/v2/apm/events/search",
            body,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var list = new List<TraceSpan>();

        if (!doc.RootElement.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in dataElement.EnumerateArray())
        {
            var attributes = item.GetProperty("attributes");

            var name = attributes.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString() ?? string.Empty
                : string.Empty;

            var service = attributes.TryGetProperty("service", out var svcEl)
                ? svcEl.GetString()
                : null;

            var duration = attributes.TryGetProperty("duration", out var durEl)
                ? TimeSpan.FromMilliseconds(durEl.GetDouble())
                : TimeSpan.Zero;

            var timestamp = attributes.TryGetProperty("timestamp", out var tsEl)
                ? DateTimeOffset.FromUnixTimeMilliseconds(tsEl.GetInt64())
                : DateTimeOffset.MinValue;

            var spanAttributes = new Dictionary<string, object?>();

            if (attributes.TryGetProperty("attributes", out var attrsElement) &&
                attrsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in attrsElement.EnumerateObject())
                {
                    spanAttributes[prop.Name] = ExtractJsonValue(prop.Value);
                }
            }

            var span = new TraceSpan
            {
                Id = item.TryGetProperty("id", out var idEl)
                    ? idEl.GetString() ?? string.Empty
                    : string.Empty,
                TraceId = attributes.TryGetProperty("trace_id", out var tId)
                    ? tId.GetString() ?? string.Empty
                    : string.Empty,
                Name = name,
                Service = service,
                Duration = duration,
                StartTime = timestamp,
                Attributes = spanAttributes
            };

            list.Add(span);
        }

        return list;
    }

    private static object? ExtractJsonValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.ToString()
        };

    public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        //TODO: Implement metrics retrieval from Datadog
        return Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());
    }
}

